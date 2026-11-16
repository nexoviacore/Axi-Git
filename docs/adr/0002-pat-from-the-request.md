# ListDirectories takes the PAT from the request

Supersedes [0001](0001-pat-from-server-config.md).

ListDirectories now authenticates to GitHub with the Personal Access Token sent in its request body as `token`, alongside `account`, `repo` and `managerType`. AxiCmdLine keeps one PAT per Manager Type (`configure <Product|Plugin|ChangeRequest> PAT`) and sends the matching one, so each Manager Type's Repository can be read with its own token instead of the single server-wide `GitHub:Token`.

## Consequences

- The PAT travels from AxiCmdLine to the AxiGIT API on every call, so the API must be reached over HTTPS. It is used only for that request's GitHub calls and is never logged or returned.
- A PAT GitHub rejects (HTTP 401) is the caller's problem and returns 401, not 502.
- `GitHub:Token` in server configuration is no longer used by ListDirectories; push and pull still read it.
- ListDirectories and ListRepositories now both take the token in the request body.
