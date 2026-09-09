# Grev Home appliance lifecycle

This document covers Grev Home's runtime/appliance behaviour during active feature development.

## Release boundary

Packaging, installer/updater work, Windows autostart registration, release versioning, code signing and final executable distribution are deliberately **deferred**.

Grev Home will return to those tasks only after the application is backbone-ready and feature-ready. Development work should not be shaped around a release installer before the console experience itself is complete and physically validated.

The project remains a normal WPF application for development and testing. This document is about runtime ownership and recovery, not release packaging.

## Single shell owner

Only one Grev Home process may own the shell/runtime state in a Windows session.

If a second development instance is launched while Grev Home is already running, it signals the existing instance to surface itself and then exits. This prevents duplicate runtime/session ownership.

## Startup ordering

Controller polling starts only after MainWindow has explicitly initialized its integration graph and established the initial route.

Feature initializers must not secretly initialize sibling systems. Shared runtime foundations are initialized by the shell bootstrap before feature surfaces depend on them.

## Crash evidence

Grev Home writes fatal exception details to:

`C:\GrevHome\Logs\grevhome-crash.log`

or the equivalent `Logs` directory under `GREV_HOME_ROOT`.

A shell-session marker is also written under `Data\Runtime`. A normal Grev Home exit removes the marker. If the next launch finds an existing marker, the previous shell did not record a clean exit and the lifecycle log records that fact.

Development builds do **not** automatically relaunch themselves after a fatal crash. Automatic restart policy belongs to the later release/appliance packaging phase.

## Runtime application recovery

Recovery of launched applications remains owned by `RuntimeSessionManager`.

On Grev Home startup it:

- loads persisted runtime session records
- revalidates process identities rather than trusting stale PIDs
- drops sessions whose processes are no longer alive
- can rediscover declared process groups for supported global apps
- deduplicates overlapping recovered sessions
- rewrites runtime state after cleanup
- does not invent playtime for an app that ended while Grev Home was not running

This recovery behaviour is part of the backbone and should be physically stress-tested before release work begins.

## Sleep and resume

The existing MainWindow remains the Grev Home shell across Windows sleep/resume.

On resume Grev Home refreshes controller configuration and runtime/session surfaces and reasserts borderless maximized presentation when MainWindow is the active console surface.

If a tracked external app currently owns the console surface and MainWindow is deliberately hidden, resume must not force Grev Home over that app.

## Deferred until feature readiness

The following are intentionally parked:

- final executable packaging/distribution
- self-contained publish profiles
- application installer/upgrader
- application uninstaller
- Windows startup registration
- release versioning
- rollback installer slots
- release CI/payload validation
- code signing
- graphical installer/updater
- Explorer shell replacement

When Grev Home reaches backbone + feature readiness, these will be designed against the final application/data contracts rather than constraining development prematurely.
