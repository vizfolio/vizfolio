using System.Net;
using System.Net.Http.Json;
using System.Text;
using Shouldly;
using Vizfolio.Api.Endpoints.Portfolios;
using Vizfolio.Application.PortfolioImports.Models;

namespace Vizfolio.Api.Tests.Endpoints.Portfolios;

public sealed class PortfolioEndpointTests : IClassFixture<VizfolioApiFactory>
{
    private const string CanonicalCsv =
        "Date,Type,Ticker,Quantity,Price,Amount,Fees,Currency,Memo,ExternalId\n" +
        "2026-06-01,Buy,VOO,2,500,-1000.00,0,USD,Buy VOO,ext-1\n" +
        "2026-06-02,Dividend,VOO,,,5.25,,USD,Q2 div,ext-2\n";

    private readonly HttpClient _client;

    public PortfolioEndpointTests(VizfolioApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task POST_portfolio_creates_and_returns_201()
    {
        var response = await _client.PostAsJsonAsync("/portfolios", new CreatePortfolioRequest("E2E Portfolio"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<PortfolioResponse>();
        body.ShouldNotBeNull();
        body.PortfolioId.ShouldNotBe(Guid.Empty);
        body.Name.ShouldBe("E2E Portfolio");
        body.AccountCount.ShouldBe(0);
    }

    [Fact]
    public async Task POST_account_under_unknown_portfolio_returns_404()
    {
        var response = await _client.PostAsJsonAsync(
            $"/portfolios/{Guid.NewGuid()}/accounts",
            new CreateAccountRequest(Guid.Empty, "Brokerage", "Fidelity", "1234", "Brokerage"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_account_duplicate_institution_and_number_returns_409()
    {
        var portfolio = await CreatePortfolioAsync();

        var first = await _client.PostAsJsonAsync(
            $"/portfolios/{portfolio.PortfolioId}/accounts",
            new CreateAccountRequest(portfolio.PortfolioId, "A1", "Fidelity", "1234", "Brokerage"));
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await _client.PostAsJsonAsync(
            $"/portfolios/{portfolio.PortfolioId}/accounts",
            new CreateAccountRequest(portfolio.PortfolioId, "A2", "Fidelity", "1234", "Brokerage"));

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task POST_import_csv_inserts_transactions_and_is_idempotent()
    {
        var portfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(portfolio.PortfolioId, accountNumber: "9001");

        var first = await UploadCsvAsync(portfolio.PortfolioId, account.AccountId, CanonicalCsv);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstResult = await first.Content.ReadFromJsonAsync<PortfolioImportResult>();
        firstResult.ShouldNotBeNull();
        firstResult.Inserted.ShouldBe(2);
        firstResult.Skipped.ShouldBe(0);
        firstResult.SourceSystem.ShouldBe("CSV");

        var second = await UploadCsvAsync(portfolio.PortfolioId, account.AccountId, CanonicalCsv);
        var secondResult = await second.Content.ReadFromJsonAsync<PortfolioImportResult>();
        secondResult.ShouldNotBeNull();
        secondResult.Inserted.ShouldBe(0);
        secondResult.Skipped.ShouldBe(2);
    }

    [Fact]
    public async Task POST_import_unsupported_file_returns_415()
    {
        var portfolio = await CreatePortfolioAsync();
        var account = await CreateAccountAsync(portfolio.PortfolioId, accountNumber: "9002");

        var response = await UploadAsync(
            portfolio.PortfolioId, account.AccountId, "random.txt", "this is not a known format\n");

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task POST_import_missing_account_returns_404()
    {
        var portfolio = await CreatePortfolioAsync();

        var response = await UploadCsvAsync(portfolio.PortfolioId, Guid.NewGuid(), CanonicalCsv);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<PortfolioResponse> CreatePortfolioAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/portfolios", new CreatePortfolioRequest($"P-{Guid.NewGuid():N}"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PortfolioResponse>())!;
    }

    private async Task<AccountResponse> CreateAccountAsync(Guid portfolioId, string accountNumber)
    {
        var response = await _client.PostAsJsonAsync(
            $"/portfolios/{portfolioId}/accounts",
            new CreateAccountRequest(portfolioId, $"A-{accountNumber}", "Fidelity", accountNumber, "Brokerage"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>())!;
    }

    private Task<HttpResponseMessage> UploadCsvAsync(Guid portfolioId, Guid accountId, string content)
        => UploadAsync(portfolioId, accountId, "sample.csv", content);

    private async Task<HttpResponseMessage> UploadAsync(Guid portfolioId, Guid accountId, string fileName, string content)
    {
        using var multipart = new MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes(content);
        var fileContent = new ByteArrayContent(fileBytes);
        multipart.Add(fileContent, "File", fileName);
        return await _client.PostAsync($"/portfolios/{portfolioId}/accounts/{accountId}/imports", multipart);
    }
}
