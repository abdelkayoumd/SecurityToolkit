# Security Toolkit

An [XrmToolBox](https://www.xrmtoolbox.com/) plugin: one place for Dataverse / Dynamics 365 security administration. See who has which security roles and teams, compare users, find out which roles grant a privilege, spot forgotten users, roles and teams, and change security safely, with every change reviewed first and undoable.

![Users and teams: a user's direct roles and the roles inherited from their teams](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/users-and-teams.png)

*Screenshots use a fictional demo organization.*

## What it does

The menu on the left has two groups. **EXPLORE** only reads. **CHANGE** modifies security in your organization, and is marked so.

### Explore (read-only)

| Screen | What you get |
|---|---|
| **Users and teams** | Pick users and see a tree of their direct roles and their teams, with the roles each team gives them. Green keys are assigned directly, orange keys are inherited from a team. |
| **Team members** | Pick one or more teams and list their members. |
| **Compare users** | Pick two users and see the differences side by side: business unit, security roles (however each user has them) and teams. |
| **Users by role** | Pick one or more roles and see everyone who has them, directly or through a team. |
| **Users by business unit** | Pick business units, optionally with their children, and see each user's teams, direct roles and roles inherited from teams. |
| **Privilege finder** | Pick tables, a privilege (Create, Read, Write, Delete, Append, Append To, Assign, Share) and a minimum depth, and see which roles grant it. |
| **Cleanup report** | Six checks for things that look forgotten: active users with no role, disabled users still in teams or holding roles, roles nobody holds, empty teams and teams that carry no role. |

![Comparing two users](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/compare-users.png)

Every list can be searched, sorted by clicking a column title, and filtered by value with the funnel in each column header. Everything you see can be exported to Excel.

![Which roles grant Delete on Account](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/privilege-finder.png)

![The cleanup report](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/cleanup-report.png)

### Change

| Screen | What it does |
|---|---|
| **Assign roles to users** | Add or remove security roles for the selected users. |
| **Assign roles to teams** | Add or remove security roles for the selected teams. |
| **Manage team membership** | Add the selected users to, or remove them from, the selected teams. |
| **Clone user profile** | Copy one user's business unit, roles and teams onto other users. |

![Assigning roles: the right-hand list shows how many of the selected users already have each role](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/assign-roles.png)

## Changing security safely

Every change goes through the same steps:

1. **Review first.** A dialog lists every change before anything is sent. Nothing happens until you click Apply.
2. **Notes explain what was skipped**, for example "this role has no copy in that user's business unit" or "already has it".
3. **Restore file.** Optionally saves a spreadsheet of how to put everything back.
4. **Per-change results.** Each change is shown as done or failed, with Dataverse's error message.
5. **Undo.** Offered after every run, and reviewed like any other change.

![The review every change goes through](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/review-changes.png)

Rules the toolkit follows so it only asks Dataverse for things that make sense:

- A security role has one copy per business unit, so a user or team can only be given the copy in **their own** business unit. If there isn't one, that change is skipped with a reason.
- Team membership is only changed for plain **owner teams**. Members of access teams, Entra group teams and each business unit's default team are managed by Dataverse and are left alone.
- Moving a user to another business unit (an option when cloning) **removes all of their current roles**, and the toolkit cannot undo that step. The review warns about it in red.
- Your account needs permission to make the changes you ask for. Dataverse refuses the ones you don't have, and the result list shows which.

Please **try the Change screens on a development environment first.**

## Install

Once it is listed in XrmToolBox's **Tool Library**, search for **Security Toolkit** there and install it. (The listing follows XrmToolBox's review of each new tool, so it can lag behind a release.)

To install by hand now, download the `DataverseSecurityToolkit` package from the [releases page](https://github.com/abdelkayoumd/SecurityToolkit/releases) (or nuget.org), take `SecurityToolkit.dll` from `lib/net48/Plugins`, and copy it into your XrmToolBox `Plugins` folder.

If Windows **Smart App Control** blocks the plugin, XrmToolBox may close when it loads. The DLL is not code-signed yet; Smart App Control blocks unsigned code on some PCs.

## Your data

The toolkit runs entirely inside XrmToolBox, on the connection you choose. It reads security records: users, roles, teams, business units and the links between them, plus (for the Privilege finder) the list of tables and which privileges each role holds. The Change screens write only the role and team assignments you review. It does not send anything to any other service, and has no telemetry.

## Build from source

```bash
dotnet build SecurityToolkit.slnx
dotnet test SecurityToolkit.Tests/SecurityToolkit.Tests.csproj
```

The tests cover the logic behind every screen, the review-apply-undo flow, and the screens themselves against a fake organization service. The screenshots above are generated by the test project against a fictional organization (`SECURITYTOOLKIT_SCREENSHOTS=1`).

Adding a screen means one class implementing `IFeature` and one line in `FeatureRegistry`.

## License

[MIT](https://github.com/abdelkayoumd/SecurityToolkit/blob/main/LICENSE)
