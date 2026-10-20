# EAMS Super Admin, demo requests and company keys

`https://<host>/Tenant/Admin` (the **operator key** from `Install-SaaS.ps1` / `Set-OperatorAccess.ps1`) is the **EAMS Super Admin** page. Only you have the operator key; customers never see this page.

## How a new customer gets a company

1. On the front page (company-code page or sign-in page) the visitor chooses **Request a demo** and fills in name, company, e-mail, phone, message.
2. You get the request: it is listed under **Demo requests** on the Super Admin page, and e-mailed to you when mail is set up (below).
3. You press **Generate key** next to the request (choose how many days it is valid, 14 by default). The page shows a key like `EAMS-7K3Q-M9XD-4TWB` **once**:
   - **Open in my e-mail program** prepares a message to the customer, or
   - **E-mail it from here** sends it (when platform mail is set up). The mail contains the key and a link that opens the registration page with the key filled in.
4. The customer opens **Create your company** (`/Register`, also linked from the front page: "I have a company key"), enters the key, chooses their **company code** and **name**, and the **administrator's e-mail and password**. The company (its own database) is created in about a minute and they sign in.

Rules of a key: it creates **one** company, only once (it is used up when the company is created; if creating fails it is given back), it expires, and you can **withdraw** an unused key. Only a hash of the key is stored: it cannot be shown again; make a new one if it is lost. You can also make a key without a request (for a customer who contacted you another way).

You can still create a company yourself on the same page ("Create a company") without a key.

Open sign-up (anyone may register without a key) stays off. It exists only if you set `Tenancy:AllowSelfRegistration` to `true`.

## Set up the e-mail (recommended)

Run on the server (any SMTP service; Microsoft 365 / Gmail / your mail relay):

```powershell
.\Set-OperatorAccess.ps1 -OperatorEmail "you@yourcompany.com" `
    -SmtpHost "smtp.office365.com" -SmtpPort 587 -SmtpSecurity StartTls `
    -SmtpUser "eams@yourcompany.com" -SmtpPassword "<password>" -SmtpFrom "eams@yourcompany.com" `
    -PublicBaseUrl "https://eams.yourcompany.com:8443"
```

- `OperatorEmail`: where you want to be told about demo requests.
- `Smtp*`: the mailbox EAMS sends from (alerts to you, keys to customers). Separate from each company's own mail settings.
- `PublicBaseUrl`: the address customers use, for the link in the key e-mail (otherwise the address you are using).

Without mail set up everything still works: requests are listed on the page and you send the key yourself.

## Safeguards

- The request form and the registration form are limited to 8 attempts per address and hour; the request form has a hidden field that catches bots.
- A key has 60 bits of randomness, so it cannot be guessed.
- Every key made or withdrawn is in the operator activity log.

The catalog gained two tables for this; `Install-SaaS.ps1 -Update` creates them.
