using Azure;
using Azure.AI.OpenAI;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Polly;
using System.Collections.Concurrent;

public interface IDocumentSimilarityService
{
    Task<float> CalculateSimilarityAsync(string doc1Id, string doc2Id);
}

public class AzureDocumentSimilarityService : IDocumentSimilarityService
{
    private readonly OpenAIClient _openAIClient;
    private readonly DocumentAnalysisClient _documentAnalysisClient;
    private readonly SecretClient _secretClient;
    private readonly IAsyncPolicy _resiliencyPolicy;
    private readonly string _embeddingDeploymentName = "text-embedding-ada-002";
    private const int MaxRetries = 3;

    public AzureDocumentSimilarityService()
    {
        var keyVaultUri = new Uri("https://your-keyvault.vault.azure.net/");
        _secretClient = new SecretClient(keyVaultUri, new DefaultAzureCredential());
        
        _openAIClient = new OpenAIClient(
            new Uri(GetSecret("OpenAIEndpoint")),
            new DefaultAzureCredential());

        _documentAnalysisClient = new DocumentAnalysisClient(
            new Uri(GetSecret("ComputerVisionEndpoint")),
            new DefaultAzureCredential());

        _resiliencyPolicy = Policy.Handle<RequestFailedException>()
            .WaitAndRetryAsync(MaxRetries, retryAttempt => 
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    private string GetSecret(string secretName) => 
        _secretClient.GetSecret(secretName).Value.Value;

    public async Task<float> CalculateSimilarityAsync(string doc1Id, string doc2Id)
    {
        var embeddings = await Task.WhenAll(
            GetDocumentEmbeddingAsync(doc1Id),
            GetDocumentEmbeddingAsync(doc2Id)
        );

        return CosineSimilarity(embeddings[0], embeddings[1]);
    }

    private async Task<float[]> GetDocumentEmbeddingAsync(string documentId)
    {
        return await _resiliencyPolicy.ExecuteAsync(async () =>
        {
            var documentText = await ProcessDocumentAsync(documentId);
            var response = await _openAIClient.GetEmbeddingsAsync(
                _embeddingDeploymentName,
                new EmbeddingsOptions(documentText)
            );
            
            return response.Value.Data[0].Embedding.ToArray();
        });
    }

    private async Task<string> ProcessDocumentAsync(string documentId)
    {
        var (textContent, imagePaths) = await ExtractDocumentParts(documentId);
        var ocrResults = await Task.WhenAll(imagePaths.Select(ProcessImageAsync));
        return textContent + " " + string.Join(" ", ocrResults);
    }

    private async Task<(string Text, List<string> Images)> ExtractDocumentParts(string documentId)
    {
        // Implementation to extract text and image paths from document
        // This would connect to your document storage and parsing system
        return ("document text", new List<string> { "image1.jpg", "image2.png" });
    }

    private async Task<string> ProcessImageAsync(string imagePath)
    {
        var imageBytes = await GetImageFromStorageAsync(imagePath);
        using var stream = new MemoryStream(imageBytes);
        
        var operation = await _documentAnalysisClient.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-read",
            stream);

        return operation.Value.Content;
    }

    private float CosineSimilarity(float[] vectorA, float[] vectorB)
    {
        float dotProduct = 0;
        float magnitudeA = 0;
        float magnitudeB = 0;
        
        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            magnitudeA += vectorA[i] * vectorA[i];
            magnitudeB += vectorB[i] * vectorB[i];
        }
        
        magnitudeA = MathF.Sqrt(magnitudeA);
        magnitudeB = MathF.Sqrt(magnitudeB);
        
        return dotProduct / (magnitudeA * magnitudeB);
    }

    private async Task<byte[]> GetImageFromStorageAsync(string imagePath)
    {
        // Implementation to retrieve image from Azure Blob Storage
        return Array.Empty<byte>();
    }
}

// Advanced Usage with Caching
public class CachedDocumentSimilarityService : IDocumentSimilarityService
{
    private readonly IDocumentSimilarityService _innerService;
    private readonly ConcurrentDictionary<string, float[]> _embeddingCache;

    public CachedDocumentSimilarityService(IDocumentSimilarityService innerService)
    {
        _innerService = innerService;
        _embeddingCache = new ConcurrentDictionary<string, float[]>();
    }

    public async Task<float> CalculateSimilarityAsync(string doc1Id, string doc2Id)
    {
        var embeddings = await Task.WhenAll(
            GetCachedEmbedding(doc1Id),
            GetCachedEmbedding(doc2Id)
        );

        return CosineSimilarity(embeddings[0], embeddings[1]);
    }

    private async Task<float[]> GetCachedEmbedding(string documentId)
    {
        return await _embeddingCache.GetOrAddAsync(documentId, 
            async key => await _innerService.GetDocumentEmbeddingAsync(key));
    }
    
    // CosineSimilarity implementation same as before
}
