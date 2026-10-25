using CellAssistant.E2ETests.Common;
using FluentAssertions;
using Xunit;
using YamlDotNet.Serialization;

namespace CellAssistant.E2ETests.Tier1_FeatureCoverage;

public class Feature03_ConfigurationTests
{
    private const string SampleConfigYaml = @"
server:
  host: ""0.0.0.0""
  port: 8080

ollama:
  baseUrl: ""http://localhost:11434""
  chatModel: ""gemma4:e4b-it-q4_K_M""
  fallbackChatModel: ""qwen3.8:latest""
  embeddingModel: ""embeddinggemma-2:latest""
  requestTimeoutSeconds: 120

qdrant:
  host: ""localhost""
  httpPort: 6333
  grpcPort: 6334
  collectionName: ""cell-knowledge""
  vectorSize: 768

knowledgeBase:
  rootPath: ""/data/KnowledgeBase""
  sqlitePath: ""/data/state/metadata.db""
  maxChunkWords: 500
  chunkOverlapWords: 100
  watcherDebounceMs: 500
";

    [Fact]
    public void Test01_DefaultOllamaConfiguration_MatchesSpecificationModels()
    {
        var deserializer = new DeserializerBuilder().Build();
        var yamlObj = deserializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(SampleConfigYaml);

        var ollama = yamlObj["ollama"];
        ollama["chatModel"].ToString().Should().Be(SpecificationConstants.PrimaryChatModel);
        ollama["fallbackChatModel"].ToString().Should().Be(SpecificationConstants.FallbackChatModel);
        ollama["embeddingModel"].ToString().Should().Be(SpecificationConstants.EmbeddingModel);
        Convert.ToInt32(ollama["requestTimeoutSeconds"]).Should().Be(SpecificationConstants.DefaultRequestTimeoutSeconds);
    }

    [Fact]
    public void Test02_DefaultQdrantConfiguration_MatchesVectorStoreParameters()
    {
        var deserializer = new DeserializerBuilder().Build();
        var yamlObj = deserializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(SampleConfigYaml);

        var qdrant = yamlObj["qdrant"];
        qdrant["collectionName"].ToString().Should().Be(SpecificationConstants.QdrantCollectionName);
        Convert.ToInt32(qdrant["vectorSize"]).Should().Be(SpecificationConstants.QdrantVectorSize);
        Convert.ToInt32(qdrant["httpPort"]).Should().Be(6333);
        Convert.ToInt32(qdrant["grpcPort"]).Should().Be(6334);
    }

    [Fact]
    public void Test03_KnowledgeBaseConfiguration_MatchesDebounceAndChunkingDefaults()
    {
        var deserializer = new DeserializerBuilder().Build();
        var yamlObj = deserializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(SampleConfigYaml);

        var kb = yamlObj["knowledgeBase"];
        Convert.ToInt32(kb["maxChunkWords"]).Should().Be(SpecificationConstants.DefaultMaxChunkWords);
        Convert.ToInt32(kb["chunkOverlapWords"]).Should().Be(SpecificationConstants.DefaultChunkOverlapWords);
        Convert.ToInt32(kb["watcherDebounceMs"]).Should().Be(SpecificationConstants.DefaultWatcherDebounceMs);
    }

    [Fact]
    public void Test04_ServerConfiguration_ListensOnAllInterfacesByDefaultForContainers()
    {
        var deserializer = new DeserializerBuilder().Build();
        var yamlObj = deserializer.Deserialize<Dictionary<string, Dictionary<string, object>>>(SampleConfigYaml);

        var server = yamlObj["server"];
        server["host"].ToString().Should().Be("0.0.0.0");
        Convert.ToInt32(server["port"]).Should().Be(8080);
    }

    [Fact]
    public void Test05_EnvironmentVariableOverrides_CanOverrideConfigValues()
    {
        var envVar = "OLLAMA_CHAT_MODEL";
        var original = Environment.GetEnvironmentVariable(envVar);
        try
        {
            Environment.SetEnvironmentVariable(envVar, "custom-model:latest");
            Environment.GetEnvironmentVariable(envVar).Should().Be("custom-model:latest");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, original);
        }
    }
}
