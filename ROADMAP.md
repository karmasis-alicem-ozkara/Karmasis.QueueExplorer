# KarmasisQueueExplorer Roadmap

KarmasisQueueExplorer is an in-house Windows desktop application for browsing, viewing, and managing MSMQ queues with an explorer-like WPF interface.

## Technical Baseline

- **Repository:** `C:\Users\AliCemÖzkara\Desktop\KarmasisQueueExplorer`
- **Runtime:** .NET 8
- **UI:** WPF, modern dark theme
- **Architecture:** MVVM with CommunityToolkit.Mvvm
- **Queue System:** MSMQ, local machine first
- **Target OS:** Windows 10/11 with MSMQ Windows Feature enabled

## Phase 1 - Scaffolding and Local Queue Navigation

**Goal:** Start the application shell and list local MSMQ queues.

- Create solution and projects:
  - `src/KarmasisQueueExplorer.App`
  - `src/KarmasisQueueExplorer.Core`
  - `tests/KarmasisQueueExplorer.Tests`
- Add dependency injection setup.
- Add base dark theme resources.
- Add core models for queue metadata.
- Add `IMsmqService` abstraction.
- Implement local MSMQ queue enumeration.
- Add `MainViewModel` and queue tree state.
- Add a left-side TreeView for local private/public/system queues.

**Exit Criteria:** The application opens and displays local MSMQ queues in the tree.

## Phase 2 - Message Listing and Body Viewer

**Goal:** Display messages from the selected queue and inspect message bodies.

- Add message metadata model.
- Add message list service methods using non-destructive peek operations.
- Add DataGrid with Label, Id, Sent Time, Body Size, Priority, and Class columns.
- Read message labels and body text directly from MSMQ so incoming messages are understandable without external tools.
- Show a readable body preview in the list and full readable body in the detail panel.
- Add detail panel with XML, JSON, Text, and Hex body viewers.
- Load message bodies on demand when a message is selected.
- Add basic formatting services for XML/JSON/Text/Hex.

**Exit Criteria:** Selecting a queue shows messages, and selecting a message displays its body and properties.

## Phase 3 - Message Operations

**Goal:** Support controlled message management actions.

- Copy messages between queues.
- Move messages between queues.
- Delete selected messages with confirmation.
- Purge a queue with confirmation.
- Save message body to file.
- Send/load messages from file.
- Add toolbar and context menu commands.
- Keep destructive actions behind `IDialogService` confirmations.

**Exit Criteria:** Users can safely copy, move, delete, purge, save, and send messages.

## Phase 4 - Auto-Refresh and Live Monitoring

**Goal:** Monitor queue changes without manual refresh.

- Add configurable auto-refresh intervals.
- Refresh queue counts and selected queue messages asynchronously.
- Automatically refresh the selected queue so newly arrived messages appear without using right-click refresh.
- Keep manual refresh as an explicit toolbar action, but do not require it for normal monitoring.
- Highlight changed queues/messages.
- Add status bar with last refresh time.
- Support cancellation when selection changes or refresh stops.

**Exit Criteria:** Auto-refresh updates counts and message lists without blocking the UI.

## Phase 5 - Filtering and Search

**Goal:** Make large queues searchable and manageable.

- Add quick filter textbox.
- Add advanced filters for label, body content, date range, priority, and size.
- Add regex support for body search.
- Use `ICollectionView` for filtering/sorting.
- Persist filter presets.

**Exit Criteria:** Users can quickly find relevant messages in large queues.

## Phase 6 - Remote Machine Support and Profiles

**Goal:** Connect to remote MSMQ machines after the local MVP is stable.

- Add connection dialog.
- Add connection profiles.
- Support machine name/IP connection targets.
- Handle MSMQ remote access errors gracefully.
- Display multiple connections in the queue tree.

**Exit Criteria:** Users can browse local and remote MSMQ queues from one application.

## Phase 7 - Production Polish

**Goal:** Prepare the application for internal production use.

- Refine dark theme visuals.
- Add keyboard shortcuts.
- Add layout persistence.
- Add structured logging with Serilog.
- Add packaging/publish profile.
- Add user documentation and troubleshooting notes.

**Exit Criteria:** The application is installable, documented, and ready for internal users.

## Safety Rules

- Browsing must use peek operations only.
- `Receive` is allowed only for explicit destructive workflows such as move/delete.
- Destructive operations require confirmation.
- MSMQ access must stay behind `IMsmqService`.
- UI must remain responsive; long-running work must be async and cancellable.
