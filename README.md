# Security Toolkit

An [XrmToolBox](https://www.xrmtoolbox.com/) tool for Dataverse / Dynamics 365 security administration: see who
has which security roles and teams, compare users, find which roles grant a privilege, spot forgotten users,
roles and teams, and change security safely, with every change reviewed first and undoable.

![Users and teams: a user's direct roles and the roles inherited from their teams](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/users-and-teams.png)

*Screenshots use a fictional demo organization.*

## Features

The menu on the left is grouped by what you want to do: **Look up**, **Understand access**, **Compare** and **Review** only read, and **Change** modifies security and is marked so (and tinted amber). The groups fold, and a "Find a screen" box filters the menu as you type.

- **Users and teams** – pick users and see a tree of their direct roles and their teams, with the roles each
  team gives them. Green keys are assigned directly, orange keys are inherited from a team.
- **Teams and members, Role holders** – pick teams or roles and see who is in them or who holds them, with each
  user's teams, direct roles and inherited roles.
- **Business units** – the business-unit hierarchy as a tree, each unit with how many users and teams it holds
  (and how many including its children). Select a unit and choose what to list: its users with their teams and
  roles, its teams, or the security roles in use there. Include child units, and export the list or the users in
  detail.
- **Two users (compare)** – pick two users and see the differences side by side: business unit, security roles
  (however each user has them) and teams. Differences only by default.
- **Two roles (compare)** – pick two security roles and see, table by table and privilege by privilege, what only
  one of them grants and where one reaches further. Differences only by default.
- **Privilege finder** – pick tables, a privilege (Create, Read, Write, Delete, Append, Append To, Assign,
  Share) and a minimum depth, and see which roles grant it.
- **Explain access** – "why can this user delete accounts?" Pick users, a privilege and a table, and see every
  route to it: which role, whether the user holds it directly or through which team, and how far it reaches,
  with the strongest route marked.
- **A user's access** ("what can this user do?") – pick a user and see every table against the eight privileges, each cell showing how
  far their roles reach (own records, business unit, parent and child units, or the whole organization),
  drawn with the same icons and wording as the Dynamics 365 security role editor. The reverse of Explain access.
- **Privileged users** – a security review in six checks: which roles can delete across the whole organization
  (measured by what the role can do, so it works in any language), who holds them directly or through a team,
  the built-in System Administrators and Customizers, disabled users and service accounts that still hold them,
  and users who have collected eight or more roles.
- **Cleanup report** – six checks for things that look forgotten: active users with no role, disabled users
  still in teams or holding roles, roles nobody holds, empty teams, and teams that carry no role. For disabled
  users you can act on the findings: take them out of their teams or remove their roles, through the same
  review, restore file and undo as every other change.
- **Roles for users, Roles for teams, Team membership, Clone a user** – add or remove security roles for users and teams,
  add or remove users from teams, and copy one user's business unit, roles and teams onto others.
- **Safe changes** – every change is listed for review before anything is sent, with notes on what was
  skipped and why. You can save a restore file first, see each change's result, and undo what succeeded. Try
  the Change screens on a development environment first.
- **Rules built in** – a role can only be given as the copy in the user's or team's own business unit, and
  team membership is only changed for plain owner teams (access teams, Entra group teams and default teams are
  managed by Dataverse). Moving a user to another business unit removes all of their roles and cannot be
  undone by the tool, so the review warns about it.
- **Easy to pick up** – the Home page lists the questions you ask ("Why can this user delete accounts?", "Is
  anyone over-privileged?") and opens the screen that answers each one. Every screen says in one line what it
  does, has a "How it works" link with the steps, and tells you what to select while its results area is empty.
- **Find and filter anything** – every list is searchable, sortable by column title, and filterable by value
  from the funnel in each column header. What you see can be exported to Excel.
- **Stays in your environment** – it runs on your XrmToolBox connection, reads security records (users, roles,
  teams, business units, and for the Privilege finder the tables and role privileges), and sends nothing
  anywhere else.

## Screenshots

**Start from the question you have**

![The Home page: questions that open the screen that answers them](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/home.png)

**Compare two users**

![Comparing two users](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/compare-users.png)

**Browse the business-unit hierarchy**

![The business unit tree with the contents of a unit](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/business-unit-tree.png)

**Compare two security roles**

![Comparing Sales Manager with Salesperson](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/compare-roles.png)

**Find which roles grant a privilege**

![Which roles grant Delete on Account](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/privilege-finder.png)

**See why a user has access**

![Why Chen Wei can delete accounts: one direct route and two through teams](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/explain-access.png)

**See everything a user can do**

![A user's reach per table and privilege, in the style of the Dynamics 365 security role editor](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/user-access-matrix.png)

**Review who holds the most powerful roles**

![The privileged users report](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/privileged-users.png)

**Spot forgotten users, roles and teams**

![The cleanup report](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/cleanup-report.png)

**Assign roles: see who already has each one**

![Assigning roles: the right-hand list shows how many of the selected users already have each role](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/assign-roles.png)

**Review every change before it is applied**

![The review every change goes through](https://raw.githubusercontent.com/abdelkayoumd/SecurityToolkit/main/docs/screenshots/review-changes.png)

## License

[MIT](https://github.com/abdelkayoumd/SecurityToolkit/blob/main/LICENSE) © 2026 abdelkayoumd
