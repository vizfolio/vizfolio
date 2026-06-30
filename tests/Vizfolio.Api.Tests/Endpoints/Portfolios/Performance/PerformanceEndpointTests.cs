using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Vizfolio.Api.Endpoints.Portfolios;
using Vizfolio.Api.Endpoints.Portfolios.Performance;
using Vizfolio.Application.Performance;

namespace Vizfolio.Api.Tests.Endpoints.Portfolios.Performance;

public sealed class PerformanceEndpointTests : IClassFixture<VizfolioApiFactory>
{
    private readonly HttpClient _client;

    public PerformanceEndpointTests(VizfolioApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GET_portfolio_performance_returns_404_for_unknown_portfolio()
    {
        var response = await _client.GetAsync(
            $"/portfolios/{Guid.NewGuid()}/performance?from=2025-01-01&to=2025-12-31");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_account_performance_returns_404_when_account_not_in_portfolio()
    {
        var portfolio = await CreatePortfolioAsync();
        var otherPortfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(otherPortfolio.PortfolioId, "9100");

        var response = await _client.GetAsync(
            $"/portfolios/{portfolio.PortfolioId}/accounts/{account.AccountId}/performance?from=2025-01-01&to=2025-12-31");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_portfolio_performance_returns_400_when_from_after_to()
    {
        var portfolio = await CreatePortfolioAsync();

        var response = await _client.GetAsync(
            $"/portfolios/{portfolio.PortfolioId}/performance?from=2025-12-31&to=2025-01-01");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_portfolio_performance_returns_400_for_daily_over_year()
    {
        var portfolio = await CreatePortfolioAsync();

        var response = await _client.GetAsync(
            $"/portfolios/{portfolio.PortfolioId}/performance?from=2023-01-01&to=2025-01-01&granularity=Daily");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_portfolio_performance_returns_empty_summary_for_empty_portfolio()
    {
        var portfolio = await CreatePortfolioAsync();

        var response = await _client.GetAsync(
            $"/portfolios/{portfolio.PortfolioId}/performance?from=2025-01-01&to=2025-12-31&granularity=Monthly");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PerformanceResponse>();
        body.ShouldNotBeNull();
        body.Scope.ShouldBe(PerformanceScope.Portfolio);
        body.ScopeId.ShouldBe(portfolio.PortfolioId);
        body.Currency.ShouldBe("USD");
        body.Summary.BeginningBalance.ShouldBe(0m);
        body.Summary.EndingBalance.ShouldBe(0m);
        body.Series.Count.ShouldBe(12);
    }

    [Fact]
    public async Task GET_account_performance_returns_summary_with_deposits()
    {
        var portfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(portfolio.PortfolioId, "9101");

        // Seed two deposits via CSV import.
        const string csv =
            "Date,Type,Ticker,Quantity,Price,Amount,Fees,Currency,Memo,ExternalId\n" +
            "2025-02-15,Deposit,,,,1000.00,,USD,Initial,perf-1\n" +
            "2025-08-15,Deposit,,,,500.00,,USD,Mid year,perf-2\n";
        var uploadResult = await UploadCsvAsync(portfolio.PortfolioId, account.AccountId, "perf.csv", csv);
        uploadResult.EnsureSuccessStatusCode();

        var response = await _client.GetAsync(
            $"/portfolios/{portfolio.PortfolioId}/accounts/{account.AccountId}/performance?from=2025-01-01&to=2025-12-31&granularity=Yearly");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PerformanceResponse>();
        body.ShouldNotBeNull();
        body.Summary.Deposits.ShouldBe(1500m);
        body.Summary.Withdrawals.ShouldBe(0m);
        body.Summary.EndingBalance.ShouldBe(1500m);
        body.Summary.BeginningBalance.ShouldBe(0m);
        body.Summary.InvestmentReturn.ShouldBe(0m);
    }

    [Fact]
    public async Task GET_portfolio_performance_uses_defaults_when_query_omitted()
    {
        var portfolio = await CreatePortfolioAsync();

        var response = await _client.GetAsync($"/portfolios/{portfolio.PortfolioId}/performance");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PerformanceResponse>();
        body.ShouldNotBeNull();
        body.Granularity.ShouldBe(PerformanceGranularity.Monthly);
        body.To.ShouldBe(DateOnly.FromDateTime(DateTime.UtcNow.Date));
    }

    private async Task<PortfolioResponse> CreatePortfolioAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/portfolios", new CreatePortfolioRequest($"Perf-{Guid.NewGuid():N}"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PortfolioResponse>())!;
    }

    private async Task<AccountResponse> CreateAccountAsync(Guid portfolioId, string accountNumber)
    {
        var response = await _client.PostAsJsonAsync(
            $"/portfolios/{portfolioId}/accounts",
            new CreateAccountRequest(portfolioId, $"A-{accountNumber}", "fidelity.com", accountNumber, "Brokerage"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }

    private async Task<HttpResponseMessage> UploadCsvAsync(Guid portfolioId, Guid accountId, string fileName, string content)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content)), "File", fileName);
        return await _client.PostAsync($"/portfolios/{portfolioId}/accounts/{accountId}/imports", multipart);
    }
}
