# Controller profile picker and Grev.dad

Who's playing uses one horizontal row, sized for four cards. Controller focus
scrolls additional profiles into view; edges fade only when more content remains.
Cards show avatars, stable profile colours, status and sign-in state. Down reaches
Create Account when authorised; Up restores the last profile selection.

Profile Edit provides Copy code and Open approval page during a link request,
plus Open Grev.dad. The Home Grev.dad tile uses the normal customisation flow.

The in-app browser uses Microsoft WebView2 with a separate user-data folder for
each local GrevID. It is disposed on navigation away or a Primary User change.
Website cookies persist for that profile; they are never shared across profiles.
Only the configured HTTPS Grev.dad origin is allowed. Downloads and permission
prompts are denied; external links are not automatically opened or trusted.

Choose Browse page: D-pad next/previous, A activate, B browser toolbar. Text fields
open the shared keyboard; Symbols supports email/password punctuation. Password
preview is masked and cleared on close; the existing page password is never read.
Dropdowns use controller-selectable choices. Submitting a form requires a separate
activation, not merely finishing typing. Back to Grev Home restores the source
screen; the existing link poll resumes when Profile Edit is reopened.

Requires Microsoft Edge WebView2 Runtime. Missing runtime or network failures show
a recoverable message. Ordinary links/forms/dropdowns are supported, not arbitrary
canvas widgets, file-upload controls, external SSO or third-party verification UI.
Do not describe those unsupported paths as fully controller-tested.
