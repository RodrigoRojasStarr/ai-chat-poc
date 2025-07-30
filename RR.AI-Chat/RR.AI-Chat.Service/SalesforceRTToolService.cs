using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using RR.AI_Chat.Repository;
using System.ComponentModel;
using System.Text.Json;

namespace RR.AI_Chat.Service
{
    public interface ISalesforceRTToolService
    {
        Task<string> GetContactInformationAsync(
            [Description("Maximum number of contacts to return (default: 10, recommended range: 1-100)")] int take = 10,
            [Description("Number of contacts to skip for pagination (default: 0, use for retrieving additional pages)")] int skip = 0,
            [Description("Search term to filter contacts by name (first or last) or email. Leave empty to return all contacts.")] string? searchTerm = null,
            [Description("Filter by email address. Exact match or partial match supported.")] string? email = null,
            [Description("Filter by first name. Partial match supported.")] string? firstName = null,
            [Description("Filter by last name. Partial match supported.")] string? lastName = null,
            [Description("Include only active contacts (default: true). Set to false to include inactive contacts.")] bool activeOnly = true,
            [Description("Specific contact ID to search for. When provided, other filters are ignored.")] string? contactId = null,
            [Description("Sort order for results: 'newest' (default - newest first), 'oldest' (oldest first)")] string sortOrder = "newest",
            [Description("Filter contacts created after this date (ISO format: yyyy-MM-dd or yyyy-MM-ddTHH:mm:ss)")] DateTime? createdAfter = null,
            [Description("Filter contacts created before this date (ISO format: yyyy-MM-dd or yyyy-MM-ddTHH:mm:ss)")] DateTime? createdBefore = null,
            [Description("Filter contacts created on this specific date (ISO format: yyyy-MM-dd)")] DateTime? createdOnDate = null);

        IList<AITool> GetTools();
    }

    public class SalesforceRTToolService(ILogger<SalesforceRTToolService> logger, SalesforceRTDbContext ctx) : ISalesforceRTToolService
    {
        private readonly ILogger _logger = logger;
        private readonly SalesforceRTDbContext _ctx = ctx;

        // Cache JsonSerializerOptions instance to avoid CA1869
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        [Description("Searches and retrieves Salesforce contacts with advanced filtering, date range queries, and flexible sorting. Can search by name, email, ID, creation dates, or return all contacts. Supports getting oldest contacts ever created, newest contacts, or contacts within specific date ranges.")]
        public async Task<string> GetContactInformationAsync(
            [Description("Maximum number of contacts to return (default: 10, recommended range: 1-100)")] int take = 10,
            [Description("Number of contacts to skip for pagination (default: 0, use for retrieving additional pages)")] int skip = 0,
            [Description("Search term to filter contacts by name (first or last) or email. Leave empty to return all contacts.")] string? searchTerm = null,
            [Description("Filter by email address. Exact match or partial match supported.")] string? email = null,
            [Description("Filter by first name. Partial match supported.")] string? firstName = null,
            [Description("Filter by last name. Partial match supported.")] string? lastName = null,
            [Description("Include only active contacts (default: true). Set to false to include inactive contacts.")] bool activeOnly = true,
            [Description("Specific contact ID to search for. When provided, other filters are ignored.")] string? contactId = null,
            [Description("Sort order for results: 'newest' (default - newest first), 'oldest' (oldest first)")] string sortOrder = "newest",
            [Description("Filter contacts created after this date (ISO format: yyyy-MM-dd or yyyy-MM-ddTHH:mm:ss)")] DateTime? createdAfter = null,
            [Description("Filter contacts created before this date (ISO format: yyyy-MM-dd or yyyy-MM-ddTHH:mm:ss)")] DateTime? createdBefore = null,
            [Description("Filter contacts created on this specific date (ISO format: yyyy-MM-dd)")] DateTime? createdOnDate = null)
        {
            _logger.LogInformation("Retrieving salesforce contacts - take: {take}, skip: {skip}, searchTerm: {searchTerm}, email: {email}, firstName: {firstName}, lastName: {lastName}, activeOnly: {activeOnly}, contactId: {contactId}, sortOrder: {sortOrder}, createdAfter: {createdAfter}, createdBefore: {createdBefore}, createdOnDate: {createdOnDate}",
                take, skip, searchTerm, email, firstName, lastName, activeOnly, contactId, sortOrder, createdAfter, createdBefore, createdOnDate);

            var query = _ctx.Contacts.AsNoTracking();

            // If searching by specific ID, ignore other filters
            if (!string.IsNullOrWhiteSpace(contactId))
            {
                query = query.Where(x => x.Id == contactId.Trim());
            }
            else
            {
                // Apply standard filters only if not searching by ID
                if (activeOnly)
                {
                    query = query.Where(x => x.ContactInactive != 1);
                }

                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    var term = searchTerm.Trim();
                    query = query.Where(x =>
                        (x.FirstName != null && x.FirstName.Contains(term)) ||
                        (x.LastName != null && x.LastName.Contains(term)) ||
                        (x.Email != null && x.Email.Contains(term)));
                }

                if (!string.IsNullOrWhiteSpace(email))
                {
                    query = query.Where(x => x.Email != null && x.Email.Contains(email.Trim()));
                }

                if (!string.IsNullOrWhiteSpace(firstName))
                {
                    query = query.Where(x => x.FirstName != null && x.FirstName.Contains(firstName.Trim()));
                }

                if (!string.IsNullOrWhiteSpace(lastName))
                {
                    query = query.Where(x => x.LastName != null && x.LastName.Contains(lastName.Trim()));
                }

                // Apply date filters
                if (createdOnDate.HasValue)
                {
                    // Filter for specific date (ignoring time)
                    var dateOnly = createdOnDate.Value.Date;
                    var nextDay = dateOnly.AddDays(1);
                    query = query.Where(x => x.CreatedDate >= dateOnly && x.CreatedDate < nextDay);
                }
                else
                {
                    // Apply date range filters
                    if (createdAfter.HasValue)
                    {
                        query = query.Where(x => x.CreatedDate >= createdAfter.Value);
                    }

                    if (createdBefore.HasValue)
                    {
                        query = query.Where(x => x.CreatedDate <= createdBefore.Value);
                    }
                }
            }

            // Apply sorting
            var normalizedSortOrder = sortOrder?.ToLowerInvariant() ?? "newest";
            if (normalizedSortOrder == "oldest")
            {
                query = query.OrderBy(x => x.CreatedDate);
            }
            else
            {
                query = query.OrderByDescending(x => x.CreatedDate);
            }

            var result = await query
                            .Skip(skip)
                            .Take(take)
                            .Select(x => new
                            {
                                x.Id,
                                x.FirstName,
                                x.LastName,
                                x.Email,
                                x.Phone,
                                x.CreatedDate,
                                IsActive = x.ContactInactive != 1
                            })
                            .ToListAsync();

            return JsonSerializer.Serialize(result, _jsonOptions);
        }

        public IList<AITool> GetTools()
        {
            IList<AITool> functions = [
                AIFunctionFactory.Create(GetContactInformationAsync)];
            return functions;
        }
    }
}
