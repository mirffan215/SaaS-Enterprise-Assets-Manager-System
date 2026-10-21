# RFID labels, fixed-asset ID and maintenance costs

## RFID labels

Assets → select assets → **Labels** → design **RFID label**.

- Each asset gets its own **RFID tag number (EPC)**, 24 hex characters, the first time an RFID label or encoding file is made for it. It never changes (also not on a reprint) and is shown on the asset page under *RFID tag (EPC)*.
- **Print the label** on pre-encoded RFID inlays: it is the QR label plus the EPC as text.
- **Encoding file** (buttons on the same page) writes the EPC into blank tags with your RFID printer:
  - **CSV** (`AssetNumber, AssetName, EPC, Url`) for the printer's encoding software or a spreadsheet;
  - **ZPL** for Zebra RFID printers (`^RFW` writes the EPC, then the asset number, EPC text and QR are printed). Set label size and print position in the printer first.
- A reader or scanner app that opens `https://<site>/r/<EPC>` lands on the asset (sign-in required). Unknown EPCs give "not found".
- EPC layout: `A5` + company (6 hex) + asset (8 hex) + 8 random hex. It is private to this system, not a GS1 code.

## Fixed asset (accounting)

The Procurement tab keeps only **D365 fixed asset ID (optional)** in this section. Capitalisation date, depreciation method, fixed asset group and recorded book value are no longer shown or edited; values already stored are left untouched in the database.

## Maintenance and repair costs

- **Record work that is already done:** asset → Actions → Log maintenance → *The work is already done*. Enter the date, what was done, cost, currency and invoice number. It is saved as a completed job (the asset's status does not change).
- **Complete a scheduled job** (Maintenance → Update → Complete) now also takes the invoice number.
- **Add or correct a cost later:** Maintenance → Completed → *Add cost / Edit cost*.
- **Where it shows:** each job on the asset page, the **total maintenance and repair cost** (per currency), and under Procurement the **total cost of ownership** (purchase cost + maintenance and repairs when both are in the same currency). The Maintenance list has a Cost column with a total for the jobs listed, and the Maintenance report already carries the cost.
- Costs are shown only to people with the *view financials* permission; anyone who may manage maintenance can enter them.
