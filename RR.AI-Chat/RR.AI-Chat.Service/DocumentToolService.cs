using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RR.AI_Chat.Dto;
using RR.AI_Chat.Entity;
using RR.AI_Chat.Repository;
using System.ComponentModel;
using System.Text.Json;

namespace RR.AI_Chat.Service
{
    public interface IDocumentToolService
    {
        Task<string> GetSessionDocumentsAsync(
            string sessionId, 
            string? nameFilter = null,
            string? extensionFilter = null,
            string sortBy = "date",
            int maxResults = 50,
            bool includePageCount = true,
            bool includeDetailedInfo = false,
            CancellationToken cancellationToken = default);

        Task<string> GetDocumentOverviewAsync(
            string sessionId, 
            string documentId, 
            string overviewType = "comprehensive",
            string detailLevel = "standard",
            int? maxPages = null,
            string? focusArea = null,
            CancellationToken cancellationToken = default);
    
        IList<AITool> GetTools();

        Task<List<Document>> SearchDocumentsAsync(
            string sessionId, 
            string prompt, 
            int maxResults = 10,
            double similarityThreshold = 0.5,
            string? documentName = null,
            bool includePageNumbers = true,
            CancellationToken cancellationToken = default);
    }

    public class DocumentToolService(ILogger<DocumentToolService> logger,
         [FromKeyedServices("azureopenai")] IChatClient openAiClient,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        AIChatDbContext ctx) : IDocumentToolService
    {
        private readonly ILogger _logger = logger;
        private readonly AIChatDbContext _ctx = ctx;
        private readonly IChatClient _chatClient = openAiClient;
        private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator = embeddingGenerator;
        private const double _cosineDistanceThreshold = 0.5;

        // Cache JsonSerializerOptions instance to avoid CA1869
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };


        [Description("Retrieves all documents in the current session with filtering and sorting options. Returns document metadata including names, types, creation dates, and page counts. Useful for document discovery and selection.")]
        public async Task<string> GetSessionDocumentsAsync(
            [Description("The session ID containing the documents to retrieve (GUID format)")] string sessionId,
            [Description("Filter by document name (partial match supported). Leave empty to get all documents.")] string? nameFilter = null,
            [Description("Filter by file extension (e.g., 'pdf', 'docx', 'txt'). Leave empty for all types.")] string? extensionFilter = null,
            [Description("Sort order: 'name' (alphabetical), 'date' (newest first), 'dateOld' (oldest first), 'extension' (by file type)")] string sortBy = "date",
            [Description("Maximum number of documents to return (default: 50, range: 1-200)")] int maxResults = 50,
            [Description("Include page count for each document (default: true). Set to false for faster response.")] bool includePageCount = true,
            [Description("Include detailed metadata like file size and modification dates (default: false)")] bool includeDetailedInfo = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return "No session ID provided. Continue with your work without mentioning it.";
            }

            if (Guid.TryParse(sessionId, out var sessionGuid) == false)
            {
                return "The session ID is not a valid GUID. Continue with your work without mentioning it.";
            }

            // Validate and clamp parameters
            maxResults = Math.Clamp(maxResults, 1, 200);

            var query = _ctx.Documents.AsNoTracking()
                .Where(x => x.SessionId == sessionGuid && !x.DateDeactivated.HasValue);

            // Apply filters
            if (!string.IsNullOrWhiteSpace(nameFilter))
            {
                query = query.Where(x => x.Name.Contains(nameFilter.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(extensionFilter))
            {
                var ext = extensionFilter.Trim().TrimStart('.');
                query = query.Where(x => x.Extension.ToLower() == ext.ToLower());
            }

            // Apply sorting
            query = sortBy.ToLowerInvariant() switch
            {
                "name" => query.OrderBy(x => x.Name),
                "dateold" => query.OrderBy(x => x.DateCreated),
                "extension" => query.OrderBy(x => x.Extension).ThenBy(x => x.Name),
                _ => query.OrderByDescending(x => x.DateCreated)
            };

            // Get base document data
            var baseDocuments = await query
                .Take(maxResults)
                .Select(x => new
                {
                    x.Id,
                    x.SessionId,
                    x.Name,
                    x.Extension,
                    x.DateCreated
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (baseDocuments.Count == 0)
            {
                return "No documents found in the current session. Continue with your work without mentioning it.";
            }

            // Create enhanced document DTOs
            var documents = new List<object>();

            foreach (var doc in baseDocuments)
            {
                var documentInfo = new Dictionary<string, object>
                {
                    ["id"] = doc.Id.ToString(),
                    ["sessionId"] = doc.SessionId.ToString(),
                    ["name"] = doc.Name,
                    ["extension"] = doc.Extension,
                    ["dateCreated"] = doc.DateCreated
                };

                if (includePageCount)
                {
                    var pageCount = await _ctx.DocumentPages
                        .Where(p => p.DocumentId == doc.Id && !p.DateDeactivated.HasValue)
                        .CountAsync(cancellationToken);
                    documentInfo["pageCount"] = pageCount;
                }

                if (includeDetailedInfo)
                {
                    var lastModified = await _ctx.DocumentPages
                        .Where(p => p.DocumentId == doc.Id && !p.DateDeactivated.HasValue)
                        .OrderByDescending(p => p.DateCreated)
                        .Select(p => p.DateCreated)
                        .FirstOrDefaultAsync(cancellationToken);

                    documentInfo["lastModified"] = lastModified;
                    documentInfo["hasContent"] = await _ctx.DocumentPages
                        .AnyAsync(p => p.DocumentId == doc.Id && !p.DateDeactivated.HasValue, cancellationToken);
                }

                documents.Add(documentInfo);
            }

            var result = new
            {
                sessionId = sessionId,
                totalDocuments = documents.Count,
                appliedFilters = new
                {
                    nameFilter = nameFilter ?? "none",
                    extensionFilter = extensionFilter ?? "none",
                    sortBy,
                    maxResults
                },
                documents
            };

            return JsonSerializer.Serialize(result, _jsonOptions);
        }

        [Description("Creates a comprehensive overview or analysis of a specific document. Generates structured insights including main topics, key points, important details, and summaries. Can be customized for different overview types and detail levels.")]
        public async Task<string> GetDocumentOverviewAsync(
            [Description("The session ID containing the document (GUID format)")] string sessionId,
            [Description("The document ID to analyze (GUID format)")] string documentId,
            [Description("Type of overview to generate: 'comprehensive' (default - full analysis), 'summary' (brief overview), 'topics' (main topics only), 'insights' (key insights and findings), 'structure' (document structure and organization)")] string overviewType = "comprehensive",
            [Description("Detail level: 'brief', 'standard' (default), 'detailed'. Controls the depth of analysis.")] string detailLevel = "standard",
            [Description("Maximum number of pages to analyze (default: all pages, set limit for large documents)")] int? maxPages = null,
            [Description("Focus area for analysis (optional). Examples: 'financial data', 'technical specifications', 'conclusions', 'methodology'")] string? focusArea = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return "Session ID not provided. Continue with your work without mentioning it.";
            }

            if (Guid.TryParse(sessionId, out var sessionGuid) == false)
            {
                return "The session ID is not a valid GUID. Continue with your work without mentioning it.";
            }

            if (string.IsNullOrEmpty(documentId))
            {
                return "Document ID not provided. Continue with your work without mentioning it.";
            }

            if (Guid.TryParse(documentId, out var documentGuid) == false)
            {
                return "The document ID is not a valid GUID. Continue with your work without mentioning it.";
            }

            // Get document information first
            var document = await _ctx.Documents.AsNoTracking()
                .Where(x => x.Id == documentGuid && 
                           x.SessionId == sessionGuid && 
                           !x.DateDeactivated.HasValue)
                .Select(x => new { x.Name, x.Extension, x.DateCreated })
                .FirstOrDefaultAsync(cancellationToken);

            if (document == null)
            {
                return "Document not found in the specified session. Continue with your work without mentioning it.";
            }

            // Get document pages with optional limit
            var pagesQuery = _ctx.DocumentPages.AsNoTracking()
                .Include(x => x.Document)
                .Where(x => x.DocumentId == documentGuid && 
                           x.Document.SessionId == sessionGuid && 
                           !x.DateDeactivated.HasValue)
                .OrderBy(x => x.Number);

            if (maxPages.HasValue && maxPages.Value > 0)
            {
                pagesQuery = (IOrderedQueryable<DocumentPage>)pagesQuery.Take(maxPages.Value);
            }

            var documentPages = await pagesQuery
                .Select(x => new DocumentPage
                {
                    Id = x.Id,
                    Number = x.Number,
                    Text = x.Text,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (documentPages.Count == 0)
            {
                return "No pages found for this document. Continue with your work without mentioning it.";
            }

            var documentText = string.Join("\n\n", documentPages.Select(p =>
                $"Page {p.Number}: {p.Text}"));

            // Build system prompt based on overview type and detail level
            var systemPrompt = BuildSystemPrompt(overviewType, detailLevel, focusArea, document.Name, document.Extension);

            var context = FunctionInvokingChatClient.CurrentContext;
            var response = await _chatClient.GetResponseAsync([
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, JsonSerializer.Serialize(documentText)),
            ], new ChatOptions() { ModelId = context!.Options!.ModelId}, cancellationToken);

            return response.Messages.Last().Text ?? "No overview could be generated for this document.";
        }

        private static string BuildSystemPrompt(string overviewType, string detailLevel, string? focusArea, string documentName, string extension)
        {
            var basePrompt = $"You are an AI assistant analyzing the document '{documentName}' ({extension}). ";
            
            var typeInstructions = overviewType.ToLowerInvariant() switch
            {
                "summary" => "Create a concise summary highlighting the main points and conclusions.",
                "topics" => "Identify and list the main topics, themes, and subjects covered in the document.",
                "insights" => "Focus on extracting key insights, findings, conclusions, and actionable information.",
                "structure" => "Analyze the document's organization, structure, flow, and how information is presented.",
                _ => "Create a comprehensive analysis including main topics, key insights, important details, and overall summary."
            };

            var detailInstructions = detailLevel.ToLowerInvariant() switch
            {
                "brief" => " Keep the analysis concise and focus only on the most important elements.",
                "detailed" => " Provide an in-depth analysis with detailed explanations and comprehensive coverage.",
                _ => " Provide a balanced analysis with adequate detail and clear explanations."
            };

            var focusInstructions = !string.IsNullOrWhiteSpace(focusArea) 
                ? $" Pay special attention to content related to: {focusArea}."
                : "";

            var formatInstructions = overviewType.ToLowerInvariant() switch
            {
                "topics" => " Format as a structured list of topics with brief descriptions.",
                "insights" => " Format as key findings with supporting evidence from the document.",
                "structure" => " Format as an outline showing document organization and content flow.",
                _ => " Format with clear sections: 1. Main Topics, 2. Key Insights, 3. Important Details, 4. Overall Summary."
            };

            return basePrompt + typeInstructions + detailInstructions + focusInstructions + formatInstructions;
        }

        /// <summary>
        /// Searches for information within documents in the specified session using vector similarity search.
        /// Performs semantic search by generating embeddings for the search prompt and comparing against 
        /// document page embeddings using cosine distance.
        /// </summary>
        /// <param name="sessionId">The unique identifier of the session to search within. Must be a valid GUID.</param>
        /// <param name="prompt">The search query describing what the user is looking for in the documents.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation if needed.</param>
        /// <returns>
        /// A list of documents containing pages that match the search criteria, ordered by relevance.
        /// Each document includes its most relevant pages (up to 10 total pages across all documents).
        /// Returns an empty list if the session ID is invalid or no matching content is found.
        /// </returns>
        /// <remarks>
        /// This method uses vector embeddings to perform semantic search rather than simple text matching.
        /// Results are filtered by a cosine distance threshold of 0.5 and limited to the top 10 most relevant pages.
        /// Pages within each document are ordered by their similarity score to the search prompt.
        /// </remarks>
        [Description("Performs semantic search across documents in the current session to find relevant content based on the user's query. Uses AI-powered vector similarity to find the most relevant pages and sections. Returns structured results with document names, page numbers, and matching content.")]
        public async Task<List<Document>> SearchDocumentsAsync(
            [Description("The session ID containing the documents to search (GUID format)")] string sessionId, 
            [Description("The search query or question - describe what information you're looking for in natural language")] string prompt, 
            [Description("Maximum number of relevant pages to return (default: 10, range: 1-50)")] int maxResults = 10,
            [Description("Similarity threshold for cosine distance (default: 0.5, range: 0.1-0.9). Lower values are more strict (only very similar content), higher values are less strict (more results with lower similarity).")] double similarityThreshold = 0.7,
            [Description("Filter by specific document name (partial match supported). Leave empty to search all documents.")] string? documentName = null,
            [Description("Include page numbers in results for better context (default: true)")] bool includePageNumbers = true,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return [];
            }

            if (Guid.TryParse(sessionId, out var sessionGuid) == false)
            {
                return [];
            }

            // Validate and clamp parameters
            maxResults = Math.Clamp(maxResults, 1, 50);
            similarityThreshold = Math.Clamp(similarityThreshold, 0.1, 0.9);

            var embedding = await _embeddingGenerator.GenerateVectorAsync(prompt);
            var vector = embedding.ToArray();

            var query = _ctx.DocumentPages
                .AsNoTracking()
                .Include(p => p.Document)
                .Where(p => p.Document.SessionId == sessionGuid && 
                           !p.DateDeactivated.HasValue &&
                           !p.Document.DateDeactivated.HasValue);

            // Apply document name filter if specified
            if (!string.IsNullOrWhiteSpace(documentName))
            {
                query = query.Where(p => p.Document.Name.Contains(documentName.Trim()));
            }

            var docPages = await query
                .Where(p => EF.Functions.VectorDistance("cosine", p.Embedding, vector) <= similarityThreshold)
                .OrderBy(p => EF.Functions.VectorDistance("cosine", p.Embedding, vector))
                .Take(maxResults)
                .GroupBy(p => p.Document)
                .Select(g => new Document
                {
                    Id = g.Key.Id,
                    Name = g.Key.Name,
                    Extension = g.Key.Extension,
                    DateCreated = g.Key.DateCreated,
                    SessionId = g.Key.SessionId,
                    Pages = g.OrderBy(p => EF.Functions.VectorDistance("cosine", p.Embedding, vector))
                           .Select(p => new DocumentPage
                           {
                               Id = p.Id,
                               Number = includePageNumbers ? p.Number : 0,
                               Text = p.Text,
                           }).ToList()
                })
                .ToListAsync(cancellationToken);

            return docPages;
        }

        public IList<AITool> GetTools()
        {
            IList<AITool> functions = [
                AIFunctionFactory.Create(GetSessionDocumentsAsync),
                AIFunctionFactory.Create(GetDocumentOverviewAsync),
                AIFunctionFactory.Create(SearchDocumentsAsync)];

            return functions;
        }
    }
}