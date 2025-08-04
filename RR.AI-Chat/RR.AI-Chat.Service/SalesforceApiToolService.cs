using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RR.AI_Chat.Common;
using RR.AI_Chat.Dto;
using Salesforce.Common;
using Salesforce.Force;
using System.ComponentModel;
using System.Text.Json;

namespace RR.AI_Chat.Service
{
    public interface ISalesforceApiToolService
    {
        Task<string> GetOpportunityInformationAsync(
            string? opportunityId = null,
            string? policyNumber = null,
            string? starrUniqueId = null,
            DateTime? createdAfter = null,
            DateTime? createdBefore = null,
            string? stageName = null,
            string sortOrder = "newest",
            int take = 10,
            int skip = 0);

        IList<AITool> GetTools();
    }

    public class SalesforceApiToolService : ISalesforceApiToolService
    {
        public ForceClient? Client => _forceClient;

        private readonly AuthenticationClient _authenticationClient;
        private readonly SalesforceSettings _salesforceSettings;
        private ILogger _logger;
        private ForceClient? _forceClient;

        // Cache JsonSerializerOptions instance to avoid CA1869
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public SalesforceApiToolService(ILogger<SalesforceApiToolService> logger, IOptions<SalesforceSettings> salesforceSettings)
        {
            _authenticationClient = new AuthenticationClient();
            _salesforceSettings = salesforceSettings.Value;
            _logger = logger;
            ConnectAsync();
        }

        private void ConnectAsync()
        {
            _authenticationClient.UsernamePasswordAsync(_salesforceSettings.ClientId,
                                                        _salesforceSettings.ClientSecret,
                                                        _salesforceSettings.UserName,
                                                        $"{_salesforceSettings.Password}{_salesforceSettings.SecurityToken}",
                                                        _salesforceSettings.Endpoint.ToString()).Wait();

            if (_authenticationClient.AccessToken.Trim().Length > 0 && _authenticationClient.AccessToken != null)
            {
                _logger.LogInformation("Successfully Connected To Salesforce.");
                _forceClient = new ForceClient(_authenticationClient.InstanceUrl, _authenticationClient.AccessToken, _authenticationClient.ApiVersion);
            }
        }

        [Description("Searches and retrieves Salesforce opportunity information with advanced filtering by ID, policy number, starr_unique_id__c, dates, stage, and flexible sorting. Returns comprehensive opportunity details.")]
        public async Task<string> GetOpportunityInformationAsync(
            [Description("Specific opportunity ID to search for (18-character Salesforce ID). When provided, other filters are ignored.")] string? opportunityId = null,
            [Description("Filter by policy number. Supports exact or partial match.")] string? policyNumber = null,
            [Description("Filter by starr_unique_id__c custom field. Supports exact or partial match.")] string? starrUniqueId = null,
            [Description("Filter opportunities created after this date (ISO format: yyyy-MM-dd or yyyy-MM-ddTHH:mm:ss)")] DateTime? createdAfter = null,
            [Description("Filter opportunities created before this date (ISO format: yyyy-MM-dd or yyyy-MM-ddTHH:mm:ss)")] DateTime? createdBefore = null,
            [Description("Filter by opportunity stage name (e.g., 'Prospecting', 'Qualification', 'Closed Won', 'Closed Lost')")] string? stageName = null,
            [Description("Sort order for results: 'newest' (default - newest first), 'oldest' (oldest first)")] string sortOrder = "newest",
            [Description("Maximum number of opportunities to return (default: 10, recommended range: 1-100)")] int take = 10,
            [Description("Number of opportunities to skip for pagination (default: 0, use for retrieving additional pages)")] int skip = 0)
        {
            if (_forceClient == null)
            {
                _logger.LogError("Salesforce client is not connected. Cannot query opportunities.");
                return JsonSerializer.Serialize(new { error = "Salesforce client not connected" }, _jsonOptions);
            }

            _logger.LogInformation("Retrieving Salesforce opportunities - opportunityId: {opportunityId}, policyNumber: {policyNumber}, starrUniqueId: {starrUniqueId}, createdAfter: {createdAfter}, createdBefore: {createdBefore}, stageName: {stageName}, sortOrder: {sortOrder}, take: {take}, skip: {skip}",
                opportunityId, policyNumber, starrUniqueId, createdAfter, createdBefore, stageName, sortOrder, take, skip);

            try
            {
                // Build the SOQL query
                var selectClause = $"SELECT Id, Name, StageName, Amount, CreatedDate, LastModifiedDate, " +
                                   $"AccountId, Account.Name, Type, ICE_STATUS__C, POLICY_NUMBER_CURRENT__C, " +
                                   $"STARR_UNIQUE_ID__C, NEW_PRODUCER__C, ISSUING_OFFICE__C, LINE_OF_BUSINESS__C, " +
                                   $"PRODUCER_CONTACT_EMAIL__C, EFFECTIVE_DATE__C, EXPIRATION_DATE__C ";

                var fromClause = "FROM Opportunity";
                var whereConditions = new List<string>();

                // If searching by specific ID, ignore other filters
                if (!string.IsNullOrWhiteSpace(opportunityId))
                {
                    whereConditions.Add($"Id = '{opportunityId.Trim().Replace("'", "\\'")}'");
                }
                else
                {
                    // Apply other filters
                    if (!string.IsNullOrWhiteSpace(policyNumber))
                    {
                        whereConditions.Add($"POLICY_NUMBER_CURRENT__C LIKE '%{policyNumber.Trim().Replace("'", "\\'")}%'");
                    }

                    if (!string.IsNullOrWhiteSpace(starrUniqueId))
                    {
                        whereConditions.Add($"starr_unique_id__c LIKE '%{starrUniqueId.Trim().Replace("'", "\\'")}%'");
                    }

                    if (!string.IsNullOrWhiteSpace(stageName))
                    {
                        whereConditions.Add($"StageName = '{stageName.Trim().Replace("'", "\\'")}'");
                    }

                    // Date filters
                    if (createdAfter.HasValue)
                    {
                        whereConditions.Add($"CreatedDate >= {createdAfter.Value:yyyy-MM-ddTHH:mm:ssZ}");
                    }

                    if (createdBefore.HasValue)
                    {
                        whereConditions.Add($"CreatedDate <= {createdBefore.Value:yyyy-MM-ddTHH:mm:ssZ}");
                    }
                }

                // Build WHERE clause
                var whereClause = whereConditions.Count > 0 ? $"WHERE {string.Join(" AND ", whereConditions)}" : "";

                // Build ORDER BY clause
                var orderByClause = sortOrder?.ToLowerInvariant() switch
                {
                    "oldest" => "ORDER BY CreatedDate ASC",
                    "newest" => "ORDER BY CreatedDate DESC",
                    _ => "ORDER BY CreatedDate DESC"
                };

                // Build LIMIT and OFFSET
                var limitClause = $"LIMIT {take}";
                var offsetClause = skip > 0 ? $"OFFSET {skip}" : "";

                // Construct final query
                var soqlQuery = $"{selectClause} {fromClause} {whereClause} {orderByClause} {limitClause} {offsetClause}".Trim();

                _logger.LogInformation("Executing SOQL query: {soqlQuery}", soqlQuery);

                // Execute query
                var queryResult = await _forceClient.QueryAsync<OpportunityDto>(soqlQuery);

                if (queryResult?.Records == null)
                {
                    return JsonSerializer.Serialize(new { opportunities = new List<object>(), totalSize = 0 }, _jsonOptions);
                }

                // Transform results to a more readable format
                var opportunities = queryResult.Records.Select(record => new
                {
                    Id = record.ID,
                    Name = record.NAME,
                    StageName = record.STAGENAME,
                    Amount = record.AMOUNT,
                    CreatedDate = record.CREATEDDATE,
                    LastModifiedDate = record.LASTMODIFIEDDATE,
                    AccountId = record.ACCOUNTID,
                    Type = record.TYPE,
                    PolicyNumber = record.POLICY_NUMBER_CURRENT__C,
                    StarrUniqueId = record.STARR_UNIQUE_ID__C,
                    IceStatus = record.ICE_STATUS__C,
                    ProducerId = record.NEW_PRODUCER__C,
                    PolicyEffectiveDate = record.EFFECTIVE_DATE__C,
                    PolicyExpirationDate = record.EXPIRATION_DATE__C,
                    LineOfBusiness = record.LINE_OF_BUSINESS__C,
                    BusinessUnit = record.BUSINESS_UNIT__C,
                    IssuingOffice = record.ISSUING_OFFICE__C,
                }).ToList();

                var result = JsonSerializer.Serialize(opportunities, _jsonOptions);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying Salesforce opportunities");
                return JsonSerializer.Serialize(new { error = $"Failed to query opportunities: {ex.Message}" }, _jsonOptions);
            }
        }

        public IList<AITool> GetTools()
        {
            IList<AITool> functions = [
                AIFunctionFactory.Create(GetOpportunityInformationAsync)
            ];
            return functions;
        }
    }
}
