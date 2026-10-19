# Release 2.8.1: company-based roles and user access (Issue 5)

## Deploy (about 5 minutes; no database change)

1. **Prepare the files.** Extract `AMGH.ITInventory-Release2.8.1.zip` to `C:\AMGH\Release2.8.1\`, unblock the files, and copy `Migrations` from the 2.8 folder.
2. **Build:** `dotnet build AMGH.ITInventory.sln`
3. **Deploy:** `cd deployment\PowerShell; .\Deploy-WebApp.ps1`

## New screens

| Screen | Where | Who |
| --- | --- | --- |
| **Roles and permissions:** role list, add/edit, permission matrix | Administration → Roles and permissions | Company administrators (*Manage roles*) |
| **Users and company access:** a person's access in every company | Platform → Users and company access | Platform administrators only |
| **Access review**, with CSV export | Platform → Access review | Platform administrators only |
| User role assignment within one company | Administration → Users and roles (existing) | Company administrators |

### Roles and permissions

- **Fields:** each role has a **code** (e.g. `AssetViewer`, shown as "Asset viewer"), a **description** and a **status**. Its permissions are ticked in a matrix grouped by module, with an "all" box per module. Approval permissions are marked *approval*.
- **Scope:** roles belong to the company you are working in. Another company can have a role with the same code and different permissions.
- **Built-in roles** (System Administrator, IT Asset Manager, and so on) keep their code and stay active, but their permissions can be adjusted.
- **No lock-out:** the System Administrator role always keeps *Manage roles*, *Manage users* and *View users*, so a company can't lock itself out of its own administration.
- **Changes take effect** for everyone with the role within a few minutes (the session check interval, 5 minutes by default), without signing out.

### A person in several companies

**Platform → Users and company access → (person)** shows every company. For each one you can:

- grant or suspend access, with at least one role when access is granted;
- choose the roles that apply there;
- mark the person's default company.

For example: Mohammed is *IT Asset Manager* in AMGH and *Asset Viewer* in ABC. He can manage assets in AMGH, and in ABC he can only view them.

- **Protection:** a company can never lose its last active System Administrator.
- **Audit:** every change is recorded in the audit trail of the company it concerns.
- **Effect:** suspending access ends the person's open session in that company at the next check, and they can no longer sign in there.

**Global scope** means platform administrator, a per-person setting for the few people who manage all companies. Company administrators never get access to other companies.

### Access review

Every person × company: roles, access status, default company, platform administrator flag, and last sign-in. Export it to CSV and review it periodically with each company's owner (ISO 27001 A.5.18).

## Security changes (defence in depth)

- **Before:** company isolation relied on SQL Server Row-Level Security and the application's query filters.
- **Now, also explicit filters by company in:**
  - **Sign-in:** a person's roles and permissions are gathered for the chosen company only. One company's roles can never grant rights in another, even if an isolation layer were misconfigured.
  - **Role and access administration:** every read and write. Changing a person's roles in one company cannot touch their roles in another.
  - **New roles and role links** are created with their company set explicitly.

## How sign-in and company selection work

1. The person chooses a company workspace (the branded sign-in page), then signs in.
2. **After** the password is verified, the system checks the person has **active access to that company**. Without it, sign-in is refused, so the choice of workspace never replaces authentication.
3. People with one company go straight in; their workspace is remembered.
4. **Switch company** signs out and requires signing in to the other company, so each company gets a fresh authorisation with its own roles.
5. Changing a URL or a request to reach another company's data is refused by the application and by SQL Server. This is covered by the security tests since Release 2.0.

## Verified

- **Security tests:** 101 pass (22 new). They cover:
  - roles visible only in their own company, with codes unique per company;
  - another company's role refused, even by URL;
  - built-in role protection and the no-lock-out rule;
  - session refresh after a change;
  - Mohammed's AMGH + DEMO scenario, his default company, and the last-administrator protection;
  - removing and restoring a role without duplicates;
  - the access review and search.
- **In the running application:**
  - the technician has *IT Technician* in AMGH and *Asset Viewer* in ABC: in ABC he can view assets but **not** add them, while in AMGH he can add them;
  - platform and role pages refused to him;
  - the access review and CSV correct;
  - after his ABC access was suspended, his open ABC session ended at the next check and signing in to ABC was refused.
- **Overall:** 216 application tests and 39 security probes pass.

## Not in this release

- **"Role for category"** (e.g. a role limited to IT hardware or vehicles) comes with asset categories in **Release 2.9**, because it limits *which assets* a role covers.
- **Authenticator-app MFA** is still open (planned R2 item).
