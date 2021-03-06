﻿namespace Nancy.ViewEngines.Razor.Tests
{
    using System;
    using System.Dynamic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using FakeItEasy;
    using Xunit;
    using Nancy.Tests;
    using Nancy.ViewEngines.Razor.Tests.Models;

    public class RazorViewEngineFixture
    {
        private readonly RazorViewEngine engine;
        private readonly IRenderContext renderContext;
        private readonly IRazorConfiguration configuration;
        private readonly FileSystemViewLocationProvider fileSystemViewLocationProvider;
        private readonly IRootPathProvider rootPathProvider;

        public RazorViewEngineFixture()
        {
            this.configuration = A.Fake<IRazorConfiguration>();
            this.engine = new RazorViewEngine(this.configuration);

            var cache = A.Fake<IViewCache>();
            A.CallTo(() => cache.GetOrAdd(A<ViewLocationResult>.Ignored, A<Func<ViewLocationResult, Func<NancyRazorViewBase>>>.Ignored))
                .ReturnsLazily(x =>
                {
                    var result = x.GetArgument<ViewLocationResult>(0);
                    return x.GetArgument<Func<ViewLocationResult, Func<NancyRazorViewBase>>>(1).Invoke(result);
                });

            this.renderContext = A.Fake<IRenderContext>();
            A.CallTo(() => this.renderContext.ViewCache).Returns(cache);
            A.CallTo(() => this.renderContext.LocateView(A<string>.Ignored, A<object>.Ignored))
                .ReturnsLazily(x =>
                {
                    var viewName = x.GetArgument<string>(0);
                    return FindView(viewName); ;
                });

            this.rootPathProvider = A.Fake<IRootPathProvider>();
            A.CallTo(() => this.rootPathProvider.GetRootPath()).Returns(Path.Combine(Environment.CurrentDirectory, "TestViews"));

            this.fileSystemViewLocationProvider = new FileSystemViewLocationProvider(this.rootPathProvider, new DefaultFileSystemReader());
        }

        [Fact]
        public void GetCompiledView_should_render_to_stream()
        {
            // Given
            var location = new ViewLocationResult(
                string.Empty,
                string.Empty,
                "cshtml",
                () => new StringReader(@"@{var x = ""test"";}<h1>Hello Mr. @x</h1>")
            );

            var stream = new MemoryStream();

            // When
            var response = this.engine.RenderView(location, null, this.renderContext);
            response.Contents.Invoke(stream);

            // Then
            stream.ShouldEqual("<h1>Hello Mr. test</h1>");
        }

        [Fact]
        public void Should_be_able_to_render_view_with_partial_to_stream()
        {
            // Given
            var location = new ViewLocationResult(
                string.Empty,
                string.Empty,
                "cshtml",
                () => new StringReader(@"@{var x = ""test"";}<h1>Hello Mr. @x</h1> @Html.Partial(""partial.cshtml"")")
            );

            var partialLocation = new ViewLocationResult(
                string.Empty,
                "partial.cshtml",
                "cshtml",
                () => new StringReader(@"this is partial")
            );

            A.CallTo(() => this.renderContext.LocateView("partial.cshtml", null)).Returns(partialLocation);

            var stream = new MemoryStream();

            // When
            var response = this.engine.RenderView(location, null, this.renderContext);
            response.Contents.Invoke(stream);

            // Then
            stream.ShouldEqual("<h1>Hello Mr. test</h1> this is partial");
        }

        [Fact]
        public void Should_support_files_with_the_razor_extensions()
        {
            // Given, When
            var extensions = this.engine.Extensions;

            // Then
            extensions.ShouldHaveCount(2);
            extensions.ShouldEqualSequence(new[] { "cshtml", "vbhtml" });
        }

        [Fact]
        public void RenderView_should_accept_a_model_and_read_from_it_into_the_stream()
        {
            // Given
            var location = new ViewLocationResult(
                string.Empty,
                string.Empty,
                "cshtml",
                () => new StringReader(@"<h1>Hello Mr. @Model.Name</h1>")
            );

            var stream = new MemoryStream();

            dynamic model = new ExpandoObject();
            model.Name = "test";

            // When
            var response = this.engine.RenderView(location, model, this.renderContext);
            response.Contents.Invoke(stream);

            // Then
            stream.ShouldEqual("<h1>Hello Mr. test</h1>");
        }

        [Fact]
        public void RenderView_csharp_should_use_model_directive_for_strongly_typed_view()
        {
            // Given
            var location = FindView("ViewThatUsesModelCSharp");

            var stream = new MemoryStream();

            var model = new DateTime(2000, 1, 1);

            // When
            var response = this.engine.RenderView(location, model, this.renderContext);
            response.Contents.Invoke(stream);

            // Then
            stream.ShouldEqual("\r\n<h1>Hello at " + model.ToString("MM/dd/yyyy") + "</h1>");
        }

        [Fact]
        public void RenderView_csharp_should_be_able_to_use_a_model_from_another_assembly()
        {
            // Given
            var view = new StringBuilder()
                .AppendLine("@model Nancy.ViewEngines.Razor.Tests.Models.Person")
                .Append("<h1>Hello Mr. @Model.Name</h1>");

            var location = new ViewLocationResult(
                string.Empty,
                string.Empty,
                "cshtml",
                () => new StringReader(view.ToString())
            );

            var stream = new MemoryStream();

            var model = new Person { Name = "Jeff" };

            // When
            var response = this.engine.RenderView(location, model, this.renderContext);
            response.Contents.Invoke(stream);

            // Then
            stream.ShouldEqual("<h1>Hello Mr. Jeff</h1>");
        }

        [Fact]
        public void RenderView_csharp_should_be_able_to_use_a_using_statement()
        {
            // Given
            var view = new StringBuilder()
                .AppendLine("@model Nancy.ViewEngines.Razor.Tests.Models.Person")
                .AppendLine("@using Nancy.ViewEngines.Razor.Tests.Models")
                .AppendLine(@"@{ var hobby = new Hobby { Name = ""Music"" }; }")
                .Append("<h1>Mr. @Model.Name likes @hobby.Name!</h1>");

            var location = new ViewLocationResult(
                string.Empty,
                string.Empty,
                "cshtml",
                () => new StringReader(view.ToString())
            );

            var stream = new MemoryStream();

            var model = new Person { Name = "Jeff" };

            // When
            var response = this.engine.RenderView(location, model, this.renderContext);
            response.Contents.Invoke(stream);

            // Then
            stream.ShouldEqual("<h1>Mr. Jeff likes Music!</h1>");
        }

        [Fact]
        public void RenderView_csharp_should_be_able_to_find_the_model_when_a_null_model_is_passed()
        {
            // Given
            var view = new StringBuilder()
                .AppendLine("@model Nancy.ViewEngines.Razor.Tests.Models.Person")
                .AppendLine(@"@{ var hobby = new Hobby { Name = ""Music"" }; }")
                .Append("<h1>Mr. Somebody likes @hobby.Name!</h1>");

            var location = new ViewLocationResult(
                string.Empty,
                string.Empty,
                "cshtml",
                () => new StringReader(view.ToString())
            );

            var stream = new MemoryStream();

            A.CallTo(() => this.configuration.AutoIncludeModelNamespace).Returns(true);

            // When
            var response = this.engine.RenderView(location, null, this.renderContext);
            response.Contents.Invoke(stream);

            // Then
            stream.ShouldEqual("<h1>Mr. Somebody likes Music!</h1>");
        }

        [Fact]
        public void RenderView_csharp_should_include_namespace_of_model_if_specified_in_the_configuration()
        {
            // Given
            var view = new StringBuilder()
                .AppendLine("@model Nancy.ViewEngines.Razor.Tests.Models.Person")
                .AppendLine(@"@{ var hobby = new Hobby { Name = ""Music"" }; }")
                .Append("<h1>Mr. @Model.Name likes @hobby.Name!</h1>");

            var location = new ViewLocationResult(
                string.Empty,
                string.Empty,
                "cshtml",
                () => new StringReader(view.ToString())
            );

            var stream = new MemoryStream();

            var model = new Person { Name = "Jeff" };

            A.CallTo(() => this.configuration.AutoIncludeModelNamespace).Returns(true);

            // When
            var response = this.engine.RenderView(location, model, this.renderContext);
            response.Contents.Invoke(stream);

            // Then
            stream.ShouldEqual("<h1>Mr. Jeff likes Music!</h1>");
        }

        [Fact]
        public void RenderView_vb_should_use_model_directive_for_strongly_typed_view()
        {
            // Given
            var location = FindView("ViewThatUsesModelVB");

            var stream = new MemoryStream();

            var model = new DateTime(2000, 1, 1);

            // When
            var response = this.engine.RenderView(location, model, this.renderContext);
            response.Contents.Invoke(stream);

            // Then
            stream.ShouldEqual("\r\n<h1>Hello at " + model.ToString("MM/dd/yyyy") + "</h1>");
        }

        [Fact]
        public void Should_be_able_to_render_view_with_layout_to_stream()
        {
            // Given
            var location = FindView("ViewThatUsesLayout");

            var stream = new MemoryStream();

            // When
            var response = this.engine.RenderView(location, null, this.renderContext);
            response.Contents.Invoke(stream);

            // Then
            string output = ReadAll(stream);
            output.ShouldContainInOrder("<h1>SimplyLayout</h1>", "<div>ViewThatUsesLayout</div>");
        }

        [Fact]
        public void Should_be_able_to_render_view_with_model_and_layout_to_stream()
        {
            // Given
            var location = FindView("ViewThatUsesLayoutAndModel");

            var stream = new MemoryStream();

            dynamic model = new ExpandoObject();
            model.Name = "my test";

            // When
            var response = this.engine.RenderView(location, model, this.renderContext);
            response.Contents.Invoke(stream);

            // Then
            string output = ReadAll(stream);
            output.ShouldContainInOrder("<h1>SimplyLayout</h1>", "<div>ViewThatUsesLayoutAndModel: my test</div>");
        }

        [Fact]
        public void Should_be_able_to_render_view_with_layout_and_section_to_stream()
        {
            // Given
            var location = FindView("ViewThatUsesLayoutAndSection");

            var stream = new MemoryStream();

            // When
            var response = this.engine.RenderView(location, null, this.renderContext);
            response.Contents.Invoke(stream);

            // Then
            string output = ReadAll(stream);
            output.ShouldContainInOrder("<h1>SimplyLayout</h1>",
                                        "<div>First section in ViewThatUsesLayoutAndSection</div>",
                                        "<div>ViewThatUsesLayoutAndSection</div>");
        }


        [Fact]
        public void Should_be_able_to_render_view_with_layout_and_many_sections_to_stream()
        {
            // Given
            var location = FindView("ViewThatUsesLayoutAndManySection");

            var stream = new MemoryStream();

            // When
            var response = this.engine.RenderView(location, null, this.renderContext);
            response.Contents.Invoke(stream);

            // Then
            string output = ReadAll(stream);
            output.ShouldContainInOrder("<h1>SimplyLayout</h1>",
                                        "<div>First section in ViewThatUsesLayoutAndManySection</div>",
                                        "<div>Second section in ViewThatUsesLayoutAndManySection</div>",
                                        "<div>ViewThatUsesLayoutAndManySection</div>",
                                        "<div>Third section in ViewThatUsesLayoutAndManySection</div>");
        }

        [Fact]
        public void Should_be_able_to_render_view_with_layout_and_optional_section_to_stream()
        {
            // Given
            var location = FindView("ViewThatUsesLayoutAndOptionalSection");

            var stream = new MemoryStream();

            // When
            var response = this.engine.RenderView(location, null, this.renderContext);
            response.Contents.Invoke(stream);

            // Then
            string output = ReadAll(stream);
            output.ShouldContainInOrder("<h1>SimplyLayout</h1>",
                                        "<div>ViewThatUsesLayoutAndOptionalSection</div>");
        }

        [Fact]
        public void Should_be_able_to_render_view_with_layout_and_optional_section_with_default_to_stream() { 
            //Given
            var location = FindView("ViewThatUsesLayoutAndOptionalSectionWithDefaults");
            var stream = new MemoryStream();

            //When
            var response = this.engine.RenderView(location, null, this.renderContext);
            response.Contents.Invoke(stream);

            //Then
            string output = ReadAll(stream);
            output.ShouldContainInOrder("<h1>SectionWithDefaultsLayout</h1>",
                                        "<div>OptionalSectionDefault</div>",
                                        "<div>ViewThatUsesLayoutAndOptionalSectionWithDefaults</div>");
        }
        [Fact]
        public void Should_be_able_to_render_view_with_layout_and_optional_section_overriding_the_default_to_stream() {
            //Given
            var location = FindView("ViewThatUsesLayoutAndOptionalSectionOverridingDefaults");
            var stream = new MemoryStream();

            //When
            var response = this.engine.RenderView(location, null, this.renderContext);
            response.Contents.Invoke(stream);

            //Then
            string output = ReadAll(stream);
            output.ShouldContainInOrder("<h1>SectionWithDefaultsLayout</h1>",
                                        "<div>OptionalSectionOverride</div>",
                                        "<div>ViewThatUsesLayoutAndOptionalSectionOverridingDefaults</div>");
        }

        private string ReadAll(Stream stream)
        {
            stream.Position = 0;
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private ViewLocationResult FindView(string viewName)
        {
            var location = this.fileSystemViewLocationProvider.GetLocatedViews(new[] { "cshtml", "vbhtml" }).First(r => r.Name == viewName);
            return location;
        }
    }
}