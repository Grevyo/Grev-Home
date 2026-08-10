# Grev File Explorer — Milestone 0.8

Milestone 0.8 introduces the first internal Grev Home file browser. It is a controller-first Grev Home surface inside the existing persistent `MainWindow`; it does not automate or embed Windows Explorer.

## Files Home

The Home surface discovers available locations at runtime:

- **Test Area** — disposable Grev Home-owned sandbox for UI-only file-operation testing
- Windows Downloads
- Windows Documents
- Windows Pictures
- Grev Home machine data root
- every Windows drive that is currently ready, including fixed and removable media

The first time Files Home is opened, Grev Home creates `C:\GrevHome\TestArea` with disposable sample folders and text files. If the whole TestArea folder is later removed, Grev Home can recreate a fresh sandbox when Files Home is opened again.

Drive cards show label/type and free/total capacity where Windows reports it.

## Browser behavior

- folders and drives open inside Grev Home;
- directory entries are sorted folders first, then files alphabetically;
- file rows show size/type and last-modified time where available;
- Home returns to Files Home;
- Up opens the parent directory;
- Refresh re-enumerates the current directory;
- B walks backward through the internal Files history before returning to Dashboard;
- B also cancels the internal rename/new-folder/delete confirmation surface before leaving the folder.

The router explicitly supports repeated `Files` history entries so file navigation remains part of Grev Home navigation rather than inventing a separate Window/navigation stack.

## File operations

0.8 supports:

- create folder;
- rename file/folder;
- copy file/folder;
- move file/folder;
- paste into the current folder;
- cancel pending copy/move;
- delete file/folder with confirmation.

Copy/Move works like a controller clipboard:

```text
Select source
→ Copy or Move
→ browse to destination
→ Paste Here
```

A successful Move clears the pending transfer. A successful Copy remains available so the same source can be copied to another destination until the user cancels it.

## Safety rules

The filesystem service:

- refuses rename/copy/move/delete against a drive root;
- refuses copy/move of a directory into itself or one of its descendants;
- never silently overwrites an existing destination;
- rolls back the newly-created destination directory when a recursive copy fails part-way through where possible;
- tolerates removable/network locations disappearing during enumeration;
- reports access/IO errors in Grev Home rather than crashing the whole shell.

Delete is deliberately confirmed in an internal Grev Home overlay. **0.8 deletion is permanent and does not use the Windows Recycle Bin.** The built-in Test Area exists so destructive testing does not require touching real personal data.

## Controller text entry

New Folder and Rename use one internal on-screen keyboard with:

- letters;
- numbers;
- common filename punctuation (`-_.()`);
- uppercase/lowercase toggle;
- Space;
- Backspace;
- Save/Cancel.

Physical keyboard input remains supported.

## File opening boundary

0.8 opens **folders/drives**, but intentionally does not launch arbitrary files yet.

Opening a file through the Windows shell directly would bypass the Grev Home app catalogue/runtime/session manager. File-association launching should instead be added once Grev Home can resolve a supported file type to an App Definition and create a normal tracked LaunchSession.

This preserves the existing rule that external applications are launched and tracked centrally rather than from individual views.

## Not in 0.8

- arbitrary file launching;
- Recycle Bin integration;
- network-location discovery/credentials;
- ZIP/archive browsing/extraction;
- file search/indexing;
- thumbnails/media preview;
- multi-select/batch operations;
- permission/ACL editing;
- drive formatting/partitioning;
- SMB/NAS setup UI;
- package installation.

Those can build on the same `FileSystemService` / Files route later without replacing the 0.8 navigation model.

## Manual acceptance flow

This flow can now be completed through Grev Home itself; no CMD or Windows Explorer setup is required for the file-operation test data.

1. Dashboard → Files using controller only.
2. Verify **Test Area**, Downloads, Documents, Pictures, Grev Home Data and ready drives appear.
3. Open Test Area and verify `Folder A`, `Copy Move Test`, `Destination`, `Test File 1.txt` and `Test File 2.txt` exist.
4. Enter `Folder A` → `Folder B`, then use B repeatedly and verify it walks backward through Files history before Dashboard.
5. Use New Folder and create a mixed-case folder using the on-screen keyboard.
6. Rename that folder.
7. Select `Test File 1.txt` and choose Copy; navigate to `Destination` and Paste Here. Verify the copy appears and the Copy source remains ready.
8. Select `Test File 2.txt` and choose Move; navigate to `Copy Move Test` and Paste Here. Verify the source disappears and the Move clipboard clears.
9. Attempt to paste a copied folder into one of its own descendants; verify Grev Home rejects it.
10. Attempt to paste where an item with the same name already exists; verify Grev Home refuses to overwrite it.
11. Delete a disposable Test Area file/folder; verify the confirmation surface appears first.
12. Open Rename/Delete and press B; verify the modal cancels before folder navigation changes.
13. Connect a removable drive, refresh Files Home, browse it, then safely remove it and verify a subsequent refresh/error does not crash Grev Home.
