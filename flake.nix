{
  description = "Vizfolio — open source investment tracker (dev environment)";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
  };

  outputs =
    { self, nixpkgs }:
    let
      # Systems we support a dev shell for.
      systems = [
        "x86_64-linux"
        "aarch64-linux"
        "x86_64-darwin"
        "aarch64-darwin"
      ];
      forAllSystems = f: nixpkgs.lib.genAttrs systems (system: f nixpkgs.legacyPackages.${system});
    in
    {
      devShells = forAllSystems (pkgs: {
        default = pkgs.mkShell {
          # Backend: .NET SDK 10 (must match backend/global.json 10.0.301).
          # Frontend: Node.js 22 (Angular CLI is pinned in frontend/package.json and
          # run via npm/npx, not packaged by Nix, so the frontend stays portable
          # and easy to extract to its own repo later).
          packages = [
            pkgs.dotnetCorePackages.sdk_10_0
            pkgs.nodejs_22
          ];

          # Keep the .NET CLI from phoning home and let it find the SDK from Nix.
          env = {
            DOTNET_CLI_TELEMETRY_OPTOUT = "1";
            DOTNET_NOLOGO = "1";
            DOTNET_ROOT = "${pkgs.dotnetCorePackages.sdk_10_0}/share/dotnet";
          };

          shellHook = ''
            echo "vizfolio dev shell"
            echo "  dotnet $(dotnet --version)   node $(node --version)"
            echo "  backend:  (cd backend && dotnet run --project src/Vizfolio.Api)   (http://localhost:5261)"
            echo "  frontend: npm --prefix frontend start                            (http://localhost:4200)"
          '';
        };
      });
    };
}
