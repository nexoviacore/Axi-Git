# ListDirectories reads the PAT from server configuration, not the request

ListDirectories authenticates to GitHub with the Personal Access Token held in the AxiGIT API's own configuration (`GitHub:Token`, with the real value kept in the git-ignored `appsettings.Local.json`), and its request carries only `account`, `repo` and `managerType`. We chose this so AxiCmdLine clients never hold or transmit a PAT, and so the token can be rotated in one place without redeploying clients.

## Consequences

- This deliberately differs from ListRepositories, which still takes `Token` in its request body; that endpoint is owned separately and is intentionally left unchanged.
- Every caller sees exactly what the configured PAT can see; per-user GitHub permissions are not enforced by this endpoint.
