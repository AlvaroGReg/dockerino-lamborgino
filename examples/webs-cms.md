# WEBS-CMS example and test case

This is a real target workspace and a representative test case for the launcher, not a required project layout, set of commands, or pair of default launch actions. The user or a user-directed AI agent can use this information to enter a configuration manually through the UI or CLI. The application does not detect the layout or generate a configuration from it. Other projects may have different directories, technologies, numbers of processes, and launch types.

## Observed workspace

The examined workspace is at `../../00-WEBS-CMS`, relative to the documentation root. It contained 11 Git repositories, all with `app/package.json` and `backOffice/package.json`. Most follow `<repo>/<project>/<app|backOffice>`; EmbutidosEzequiel uses `Ezequiel` as its application directory. These observations illustrate why the user or agent must determine and explicitly provide actual paths rather than rely on application inference from repository names.

The main ASP.NET Core projects targeted `net10.0`. The frontend declared `start = npm run build:watch`. The backoffice declared `prestart = npm run combine` and `start = ng serve`; `npm start` also runs npm's `prestart` lifecycle hook. These observations are historical and should be rechecked before testing.

## Example launch types

One possible user-configured launch starts three independent terminal sessions:

1. `dotnet watch run --no-hot-reload` in the ASP.NET Core project directory.
2. `npm start` in `app`.
3. `npm start` in `backOffice`.

Another possible launch starts the backend and backoffice while the frontend session first runs `npm run update-core:latest` and runs `npm start` only if that command exits successfully. A failed update leaves its output visible and skips the dependent frontend step; independent sessions continue.

The observed `update-core:latest` script runs the core package updater and then `npm run phoenix`, which deletes `node_modules` and `package-lock.json` before `npm install`. It can take time and modify dependencies. When this particular action is configured, explain these effects without requiring confirmation. Never run it unless it is explicitly part of the selected action.

## Test coverage

Use this workspace to test explicit project registration, manual configuration through the UI and CLI, configuration persistence, three parallel terminal sessions, and a conditional sequence with both successful and failed prerequisites. Also test projects with other structures and process counts; success with WEBS-CMS alone does not establish generality.

The initial inventory did not execute startup or update commands. The [spike report](../prototype/README.md) records subsequent real startup tests and their limits; it does not establish that the update flow was tested.
