# Shared accounts and browser audit

Linking is optional on both products. Linked profiles display the website's combined XP balance and fixed 500-XP level rule; standalone profiles retain local progression. Home uploads only locally earned activity, never downloaded XP. Offline/pending Home earnings can temporarily make the displayed estimate newer than the website. Successful sync reconciles it.

Existing website awards and Home milestones share the account collection. Home milestones are mirrored with provider-qualified IDs and no second XP bonus. RetroAchievements authentication, fetching and verified award ingestion are not implemented by this change.

## Initial account test

1. Record an existing Grev.dad account's XP, level and earned achievements.
2. Link a Home profile with some completed activity to that account. Wait for successful sync and reopen the profile. Compare XP and level with the website, with no app running.
3. Complete one Home session, sync and check both products. Repeat sync and restart: XP and achievements must not duplicate.
4. Earn website XP/an achievement, open the website progression page and allow Home's next sync (normally within two minutes). Reopen Home's profile and check the balance and award.
5. Link a fresh local profile to the same website account. Cloud account data should return; no game files, emulator installations or BIOS are downloaded.
6. Switch to a different account/unlinked profile. It must not display the first account's cached awards or XP.
7. Disconnect the network, complete activity, reconnect and sync. Pending earnings should be credited once.

## Browser controller test

Open Grev's Web Browser from the dashboard. It starts at Google and does not require a website link. Select Browse page, move between links/fields using the D-pad, activate with A, type a search using the shared on-screen keyboard, then open a result. B returns to the toolbar; test back, reload, Google search and return to Home. Test controller focus after page navigation and profile switching.

The general browser permits HTTPS pages, with separate per-profile storage from the origin-restricted Grev.dad account browser. Downloads and permission prompts remain blocked. Test Google consent and normal HTML forms; arbitrary third-party canvas widgets, uploads and external sign-in flows are not guaranteed controller-compatible. WebView2 Runtime is required. Real Windows/controller validation remains a manual audit, not a CI assertion.
