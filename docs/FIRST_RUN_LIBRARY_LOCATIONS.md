# First-run library locations

First-run setup now keeps one **primary Games folder**, one **BIOS folder**, and zero or more **additional Games locations**.

- The primary Games folder remains the destination Grev-managed installers use.
- Additional Games locations are remembered for libraries spread across multiple drives.
- BIOS remains a single shared location.
- `Browse...` and `+ Add Another Games Location` use Grev Home's controller-first folder picker; paths are never typed manually.
- The picker uses the same `FileSystemService` as Grev Home Files, shows known folders/drives/favourites, and only exposes folders.
- A opens/navigates, `Use This Folder` confirms, and B walks back through the picker before cancelling at Files Home.
- Additional locations are persisted in `Data/machine-defaults.json` without breaking older version-1 files that do not contain the new optional field.
