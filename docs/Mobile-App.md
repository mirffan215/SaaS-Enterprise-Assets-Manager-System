# Mobile app (iPhone, iPad, Android)

EAMS is a **web app that installs like an app** (a Progressive Web App): its own icon, full screen, a camera scanner, no app-store account and no separate version to keep up to date. It always shows the current version of your site.

## Installing

The front page (company-code page and sign-in page) has a **Get the mobile app** box.

| Phone | What to do |
|---|---|
| **Android (Chrome)** | Tap **Install app** in the box (or Chrome menu → *Install app* / *Add to Home screen*). |
| **iPhone / iPad (Safari)** | Open the site in **Safari** → **Share** → **Add to Home Screen**. (iOS only allows this from Safari.) |
| Any | Open *Show a QR code to open it on your phone* and scan it with the phone camera. |

After installing, the app opens straight into your company (the company chosen on that phone is remembered).

## What the app does

- **Scan an asset** (menu → *Scan an asset*, also the app's long-press shortcut): the camera reads the QR label, an asset-tag barcode or an RFID tag number and opens the asset. A typed number works too, and so does a Bluetooth/USB scanner typing into the box.
- Everything else of EAMS in a phone-sized layout: asset register and details, assignments, maintenance, approvals, notifications, physical verification.
- No connection: a short "No connection" page; styles and icons open instantly. Nothing confidential is stored on the phone (signed-in pages are never cached).

## Requirements you must meet

- **A certificate the phones trust.** Installing, the camera and offline pages only work over *trusted* https. A self-signed certificate (what `Install-SaaS.ps1` creates for testing) is not trusted by phones: the page opens after a warning, but the install button, scanner and service worker are switched off by the browser. Use a certificate from a public CA (for example Let's Encrypt) on the real host name, or install your own company root CA on the phones (MDM/Intune).
- The phone must reach the site's address (office Wi-Fi/VPN for an internal server, the internet for hosting).

## Native apps in the App Store / Google Play

Not included. A store listing is possible later by wrapping this same web app (Google Play: Trusted Web Activity via PWABuilder/Bubblewrap; App Store: Capacitor) but needs your own Apple Developer (USD 99/year) and Google Play (USD 25 once) accounts, store review, and an app per company name if you want it branded. Say so when you want that step.
