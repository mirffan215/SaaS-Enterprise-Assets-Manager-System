# Schema migrations

Scripts 01-19 build a **new** company database. Every later change goes here as `NNNN_Name.sql` (0001, 0002, ...), never into 01-19.

Rules
- Safe to run twice (`IF COL_LENGTH(...) IS NULL ALTER TABLE ...`).
- Never edit a script after it has shipped; add the next number instead (the checksum of an applied script is recorded and a change is reported).
- Use `GO` between batches; `$(DatabaseName)` is available.

New companies get 01-19 and then every migration. Existing companies are upgraded when the site starts (`Tenancy:AutoUpgrade`) or with "Upgrade all companies" on the operator page; a backup is made first.
