# Session lifecycle

`SessionRegistry` is an in-memory owner. A caller supplies an optional `Project`
and an `ISession` implementation to `Open`; it generates the session ID, requests
startup and selects it on success. No project discovery, command execution,
process handling or WPF dependency is included. Implementations own their own
resources and must support cleanup by `Stop` even after a failed `Start`.

Returning from `Start` does not imply readiness: the state remains `Starting`.
After `Open` returns, the integration reports asynchronous events using
`ReportState(id, state, error)`: `Starting` may become `Running`, `Exited`, or
`Failed`; `Running` may become `Exited` or `Failed`. Only failed reports may
include an error. Invalid and late reports throw `InvalidOperationException`.
The integration must buffer events arriving before `Open` returns and deliver
them in order afterwards. The registry does not infer process exit itself.

Any listed session can be selected, including starting, exited, stopped and
failed sessions, so its output can remain visible. Stopping never changes
selection. Stopped and failed entries remain in the list. Failed starts and
stops return a failed snapshot with an error; `Stop` can be retried. A stopped
session cannot be restarted.
Unknown IDs throw `KeyNotFoundException`. The registry serializes operations,
including calls to implementations, so implementations must not block
indefinitely or call back into the registry. The application is responsible
for stopping its sessions on shutdown. Nothing persists across restarts.
