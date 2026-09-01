using Brinehold.Content;
using Xunit;

namespace Brinehold.Content.Tests
{
    public class JsonTests
    {
        [Fact]
        public void ParsesObjectsArraysAndScalars()
        {
            const string text = @"{
                ""name"": ""prototype"",
                ""count"": 42,
                ""speed"": 1.4,
                ""enabled"": true,
                ""missing"": null,
                ""list"": [1, 2, 3],
                ""nested"": { ""inner"": ""value"" }
            }";

            Assert.True(JsonValue.TryParse(text, out JsonValue json, out string error), error);
            Assert.Equal("prototype", json.GetString("name"));
            Assert.Equal(42, json.GetInt("count"));
            Assert.Equal(1400, json["speed"].AsMilli);
            Assert.True(json.GetBool("enabled"));
            Assert.Equal(3, json["list"].AsArray.Count);
            Assert.Equal("value", json["nested"].GetString("inner"));
        }

        [Fact]
        public void SupportsLineCommentsSoContentCanExplainItself()
        {
            const string text = @"{
                // A worker walks at 1.4 metres per second.
                ""moveSpeed"": 1.4  // and this is why
            }";

            Assert.True(JsonValue.TryParse(text, out JsonValue json, out string error), error);
            Assert.Equal(1400, json["moveSpeed"].AsMilli);
        }

        [Fact]
        public void DecimalsBecomeThousandthsExactly()
        {
            Assert.True(JsonValue.TryParse(@"{""a"": 0.001, ""b"": 3.2, ""c"": -1.5, ""d"": 60}",
                out JsonValue json, out _));

            Assert.Equal(1, json["a"].AsMilli);
            Assert.Equal(3200, json["b"].AsMilli);
            Assert.Equal(-1500, json["c"].AsMilli);
            Assert.Equal(60000, json["d"].AsMilli);
        }

        [Fact]
        public void MissingKeysFallBackRatherThanThrowing()
        {
            Assert.True(JsonValue.TryParse(@"{""present"": 1}", out JsonValue json, out _));
            Assert.Equal(99, json.GetInt("absent", 99));
            Assert.Equal("fallback", json.GetString("absent", "fallback"));
            Assert.False(json.Has("absent"));
            Assert.Empty(json["absent"].AsArray);
        }

        [Theory]
        [InlineData("{")]
        [InlineData("{\"a\": }")]
        [InlineData("{\"a\" 1}")]
        [InlineData("[1, 2")]
        [InlineData("{\"a\": tru}")]
        [InlineData("not json at all")]
        [InlineData("{\"a\": 1} trailing")]
        public void MalformedInputIsReportedNotThrown(string text)
        {
            Assert.False(JsonValue.TryParse(text, out _, out string error));
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void ErrorsNameTheLineSoADesignerCanFindThem()
        {
            const string text = "{\n  \"a\": 1,\n  \"b\": ,\n}";
            Assert.False(JsonValue.TryParse(text, out _, out string error));
            Assert.Contains("line 3", error);
        }

        [Fact]
        public void HandlesEscapesInStrings()
        {
            Assert.True(JsonValue.TryParse(@"{""a"": ""line\nbreak \""quoted\"" A""}", out JsonValue json, out _));
            Assert.Equal("line\nbreak \"quoted\" A", json.GetString("a"));
        }
    }
}
