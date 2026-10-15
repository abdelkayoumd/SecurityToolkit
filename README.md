# Security Toolkit

An [XrmToolBox](https://www.xrmtoolbox.com/) tool for Dataverse / Dynamics 365 security administration: see who
has which security roles and teams, compare users, find which roles grant a privilege, spot forgotten users,
roles and teams, and change security safely, with every change reviewed first and undoable.

![Users and teams: a user's direct roles and the roles inherited from their teams](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/users-and-teams.png)

*Screenshots use a fictional demo organization.*

## Features

The menu on the left has two groups: **EXPLORE** only reads, and **CHANGE** modifies security and is marked so.

- **Users and teams** – pick users and see a tree of their direct roles and their teams, with the roles each
  team gives them. Green keys are assigned directly, orange keys are inherited from a team.
- **Team members, Users by role and Users by business unit** – pick teams, roles or business units (with
  their children if you like) and see who is in them, who holds the role, and each user's teams, direct roles
  and inherited roles.
- **Compare users** – pick two users and see the differences side by side: business unit, security roles
  (however each user has them) and teams. Differences only by default.
- **Privilege finder** – pick tables, a privilege (Create, Read, Write, Delete, Append, Append To, Assign,
  Share) and a minimum depth, and see which roles grant it.
- **Cleanup report** – six checks for things that look forgotten: active users with no role, disabled users
  still in teams or holding roles, roles nobody holds, empty teams, and teams that carry no role.
- **Assign roles, manage team membership, clone a user** – add or remove security roles for users and teams,
  add or remove users from teams, and copy one user's business unit, roles and teams onto others.
- **Safe changes** – every change is listed for review before anything is sent, with notes on what was
  skipped and why. You can save a restore file first, see each change's result, and undo what succeeded. Try
  the Change screens on a development environment first.
- **Rules built in** – a role can only be given as the copy in the user's or team's own business unit, and
  team membership is only changed for plain owner teams (access teams, Entra group teams and default teams are
  managed by Dataverse). Moving a user to another business unit removes all of their roles and cannot be
  undone by the tool, so the review warns about it.
- **Find and filter anything** – every list is searchable, sortable by column title, and filterable by value
  from the funnel in each column header. What you see can be exported to Excel.
- **Stays in your environment** – it runs on your XrmToolBox connection, reads security records (users, roles,
  teams, business units, and for the Privilege finder the tables and role privileges), and sends nothing
  anywhere else.

## Screenshots

**Compare two users**

![Comparing two users](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/compare-users.png)

**Find which roles grant a privilege**

![Which roles grant Delete on Account](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/privilege-finder.png)

**Spot forgotten users, roles and teams**

![The cleanup report](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/cleanup-report.png)

**Assign roles: see who already has each one**

![Assigning roles: the right-hand list shows how many of the selected users already have each role](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/assign-roles.png)

**Review every change before it is applied**

![The review every change goes through](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/review-changes.png)

## License

[MIT](https://github.com/abdelkayoumd/SecurityToolkit/blob/main/LICENSE) © 2026 abdelkayoumd
