namespace Llm.Tests.Adapters.LLM.QueryServices.Jev;

/// <summary>
/// Jev DTO 序列化/反序列化往返测试 — 验证 JevJsonContext 源码生成器正确注册所有 DTO
/// </summary>
public class JevTypesTests {
    #region JevRequest 序列化

    [Fact]
    public void JevRequest_Serialize_ProducesCorrectJson() {
        var request = new JevRequest {
            Model = "jev-latest",
            State = JsonDocument.Parse("\"Please call me tomorrow.\"").RootElement.Clone(),
            Questions = new Dictionary<string, JevQuestion> {
                ["callback_requested"] = new JevQuestion {
                    Type = "noul",
                    Instructions = "Does the sender explicitly ask for a phone call?"
                }
            }
        };

        var json = JsonSerializer.Serialize(request, JevJsonContext.Default.JevRequest);

        json.Should().Contain("\"model\":\"jev-latest\"");
        json.Should().Contain("\"state\":\"Please call me tomorrow.\"");
        json.Should().Contain("\"callback_requested\"");
        json.Should().Contain("\"type\":\"noul\"");
        json.Should().Contain("\"instructions\":\"Does the sender explicitly ask for a phone call?");
    }

    [Fact]
    public void JevRequest_Serialize_WithChoiceQuestion_IncludesOptions() {
        var request = new JevRequest {
            Model = "jev-latest",
            State = JsonDocument.Parse("\"Customer feedback text\"").RootElement.Clone(),
            Questions = new Dictionary<string, JevQuestion> {
                ["sentiment"] = new JevQuestion {
                    Type = "choice",
                    Instructions = "Classify the sentiment",
                    Options = ["positive", "negative", "neutral"]
                }
            }
        };

        var json = JsonSerializer.Serialize(request, JevJsonContext.Default.JevRequest);

        json.Should().Contain("\"type\":\"choice\"");
        json.Should().Contain("\"options\":[\"positive\",\"negative\",\"neutral\"]");
    }

    [Fact]
    public void JevRequest_RoundTrip_PreservesAllFields() {
        var original = new JevRequest {
            Model = "jev-latest",
            State = JsonDocument.Parse("\"test state\"").RootElement.Clone(),
            Questions = new Dictionary<string, JevQuestion> {
                ["q1"] = new JevQuestion { Type = "noul", Instructions = "Is it true?" }
            }
        };

        var json = JsonSerializer.Serialize(original, JevJsonContext.Default.JevRequest);
        var deserialized = JsonSerializer.Deserialize(json, JevJsonContext.Default.JevRequest)!;

        deserialized.Model.Should().Be("jev-latest");
        deserialized.State.GetRawText().Should().Be("\"test state\"");
        deserialized.Questions.Should().HaveCount(1);
        deserialized.Questions["q1"].Type.Should().Be("noul");
        deserialized.Questions["q1"].Instructions.Should().Be("Is it true?");
    }

    #endregion

    #region JevResponse 反序列化

    [Fact]
    public void JevResponse_Deserialize_NoulAnswer_ParsesCorrectly() {
        var json = """{"id":"resp_123","model":"jev-latest","answers":{"callback_requested":{"noul":0.95,"confidence":0.9}},"usage":{"input_tokens":42}}""";

        var response = JsonSerializer.Deserialize(json, JevJsonContext.Default.JevResponse)!;

        response.Id.Should().Be("resp_123");
        response.Model.Should().Be("jev-latest");
        response.Answers.Should().HaveCount(1);
        response.Answers["callback_requested"].Noul.Should().Be(0.95);
        response.Answers["callback_requested"].Confidence.Should().Be(0.9);
        response.Answers["callback_requested"].Choice.Should().BeNull();
        response.Answers["callback_requested"].Score.Should().BeNull();
        response.Usage!.InputTokens.Should().Be(42);
    }

    [Fact]
    public void JevResponse_Deserialize_ChoiceAnswer_ParsesCorrectly() {
        var json = """{"answers":{"sentiment":{"choice":"positive","confidence":0.88}}}""";

        var response = JsonSerializer.Deserialize(json, JevJsonContext.Default.JevResponse)!;

        response.Answers["sentiment"].Choice.Should().Be("positive");
        response.Answers["sentiment"].Confidence.Should().Be(0.88);
        response.Answers["sentiment"].Noul.Should().BeNull();
    }

    [Fact]
    public void JevResponse_Deserialize_ScoreAnswer_ParsesCorrectly() {
        var json = """{"answers":{"rating":{"score":7.5,"confidence":0.92}}}""";

        var response = JsonSerializer.Deserialize(json, JevJsonContext.Default.JevResponse)!;

        response.Answers["rating"].Score.Should().Be(7.5);
        response.Answers["rating"].Confidence.Should().Be(0.92);
    }

    [Fact]
    public void JevResponse_Deserialize_MultipleQuestions_ParsesAll() {
        var json = """{"answers":{"q1":{"noul":0.8},"q2":{"choice":"yes","confidence":0.7},"q3":{"score":5.0}}}""";

        var response = JsonSerializer.Deserialize(json, JevJsonContext.Default.JevResponse)!;

        response.Answers.Should().HaveCount(3);
        response.Answers["q1"].Noul.Should().Be(0.8);
        response.Answers["q2"].Choice.Should().Be("yes");
        response.Answers["q3"].Score.Should().Be(5.0);
    }

    #endregion

    #region JevResponse 序列化(模拟 MockServer 响应)

    [Fact]
    public void JevResponse_Serialize_ProducesCorrectJson() {
        var response = new JevResponse {
            Id = "resp_456",
            Model = "jev-latest",
            Answers = new Dictionary<string, JevAnswer> {
                ["is_spam"] = new JevAnswer { Noul = 0.12, Confidence = 0.85 }
            },
            Usage = new JevUsage { InputTokens = 100 }
        };

        var json = JsonSerializer.Serialize(response, JevJsonContext.Default.JevResponse);

        json.Should().Contain("\"id\":\"resp_456\"");
        json.Should().Contain("\"model\":\"jev-latest\"");
        json.Should().Contain("\"is_spam\"");
        json.Should().Contain("\"noul\":0.12");
        json.Should().Contain("\"input_tokens\":100");
    }

    #endregion
}
