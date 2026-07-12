/**
 * Request bodies for the portfolio/account write endpoints.
 *
 * Source of truth:
 *   backend/src/Vizfolio.Api/Endpoints/Portfolios/CreatePortfolioEndpoint.cs
 *   backend/src/Vizfolio.Api/Endpoints/Portfolios/CreateAccountEndpoint.cs
 *
 * Response shapes (PortfolioSummary, AccountSummary) live in performance.models.ts.
 */

/** POST /api/portfolios */
export interface CreatePortfolioRequest {
  name: string;
}

/**
 * POST /api/portfolios/{portfolioId}/accounts
 * `portfolioId` is also in the route; the backend binds it from there, but we send it
 * in the body too so the DTO round-trips cleanly.
 */
export interface CreateAccountRequest {
  portfolioId: string;
  name: string;
  /** OFX BROKERID/BANKID, e.g. "vanguard.com". Normalized to lower-case server-side. */
  institutionCode: string;
  /** OFX ACCTID. */
  accountNumber: string;
  /** Free-form, e.g. "Brokerage", "Roth IRA". */
  accountType?: string | null;
}
