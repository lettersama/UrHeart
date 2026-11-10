# OS Character LinkMessage framework

This document defines the data contract for Character-driven `WindowUnit_LinkMessage`
interactions. Runtime data lives in `OSCharacterInteractionData.asset`; presentation
and signal matching are handled by `OSLinkMessageController`.

## Scope

- LinkMessage presents Character commentary, tutorials and required flow confirmations.
  A presentation cue may temporarily gate explicitly named
  controls for a tutorial beat, but must not lock unrelated OS interactions.
- Only one Character interaction sequence and one LinkMessage are active at a time.
- A newly activated sequence replaces the previous active sequence.
- `entryId` is a stable data ID, never a list position. Saves and special presentation
  hooks address entries with `sequenceId + entryId`.

## SO shape

```text
OSCharacterInteractionData_SO
  sequences[]
    sequenceId
    entries[]
      entryId
      triggerType
      triggerTargetId
      partType                // Text / TextWithOptions / Image / future Part
      localizationKey
      fallbackText
      replacementPolicy
      displayPolicy
      presentationCueId       // optional, identifies front-end-only effects
      requiredHeartHealProcessIndex
      requiredCompletedEntryIds[]
      options[]               // optionId, localized text, nextEntryId, resourceChange
      triggerDelay
      autoCloseDelay          // Text only; starts after DOText, 0 keeps it open
      repeatable              // false by default; one-shot is saved
      optionTimeoutDelay      // existing authored default-option timeout, 0 disables
      timeoutOptionId         // option selected by that authored timeout
      selectTimeoutOptionOnInterrupt
      allowUnansweredTimeout  // explicit opt-in; false preserves required choices
      followingMessageEntryIds[] // ordered independent messages; inspect first 3
  globalEntries[]             // same entry shape, consumed across sequences
```

A sequence is one independently activated OS flow, not necessarily a whole chapter.
A chapter may activate several sequences, or keep one sequence active while later OS
signals trigger its remaining entries.

## Trigger flow

Callers explicitly activate a sequence before the relevant gameplay transition. The
Runner is event-driven and must not poll `GameDataManager` or scene objects per frame.

Runtime systems emit semantic signals such as:

```text
OSBecameVisible
DesktopProcessPointerEnter(targetId)
DesktopProcessClicked(targetId)
WindowOpened(targetId)
WindowClosed(targetId)
ExternalSignal(targetId)
```

`DesktopProcessPointerEnter` and `DesktopProcessClicked` are emitted only by a
`DesktopProcessUnit` on the desktop or inside a Folder window. A taskbar
`ProcessUnit` does not emit these signals unless a future interaction explicitly
opts into taskbar input.

The Runner compares a signal with every unconsumed entry in the active sequence. This
allows independent one-shot reactions (for example, two desktop hover messages) to
arrive in either order. A consumed `entryId` cannot replay. If multiple entries match
the same signal, asset order decides which one is shown first.

`TextWithOptions` stays in the presenting save phase after `DOText` finishes. Clicking
an option completes the current entry and presents its `nextEntryId`. A required
option therefore resumes after loading instead of being inferred from a list index.
Displayed option labels receive a `1.` / `2.` ordinal prefix from their visual order;
data that already contains the matching prefix is left unchanged.
For a plain `Text` entry, `autoCloseDelay > 0` closes LinkMessage after the text has
finished typing and remained visible for that duration. A replacement message,
manual close, owner close or OS hide cancels the pending auto-close callback.
Global entries use their own consumed IDs, so replacing a chapter sequence does not
make a global one-shot message available again. Set `repeatable` only for a message
that is intentionally allowed to replay. To deliberately re-arm one saved one-shot,
call `SetSequenceMessageConsumed(sequenceId, entryId, false)` or
`SetGlobalMessageConsumed(entryId, false)`; re-arming an entry also reopens a sequence
that had already reached its `Completed` phase.

## Replacement policy

```text
InterruptCurrent                 // default
WaitForCurrentTextComplete       // optional
```

An unresolved sequence message with resource-changing options keeps the next
sequence message in the existing pending slot until an option is selected. This
also preserves the confirmation when the Memory reader advances to another comment.

`InterruptCurrent` immediately cancels the current `DOText`, callbacks, Tweens and Part,
then shows the incoming entry. `WaitForCurrentTextComplete` defers the incoming entry
only until the current `DOText` finishes. Questions with an active unanswered timeout
reserve the message slot from the start of their text and defer incoming messages in
a saved FIFO. Other entries retain the existing pending-slot behavior.

Minimizing the Character window is a presentation pause, not `WindowClosed`.
An option that is already visible keeps its bindings and remains selectable after
the window is restored. An entry interrupted during `DOText` is rebuilt from the
start of that saved entry when restored. A real close still emits `WindowClosed`
and may trigger a global entry.

Flow-affecting rules such as delays, signals and replacement policy belong in the SO.
Pure presentation such as text distortion or image reveal belongs to a front-end
handler selected by `sequenceId + entryId` or `presentationCueId`.

## Optional question countdown

An optional `TextWithOptions` entry can set `allowUnansweredTimeout = true`. The
countdown is enabled only when one of the first three `followingMessageEntryIds`
resolves to another `TextWithOptions` entry. Missing or empty metadata does not
enable a countdown. Required confirmations, tutorial actions and director-controlled
choices keep the default `false` unless their unanswered continuation is explicitly
supported.

`followingMessageEntryIds` describes upcoming independent Character messages in
story order. It does not include the current question's option replies, and the
runtime does not infer this order from asset positions or `nextEntryId` branches.
Branch variants occupying the same story beat count as one upcoming message.
IDs resolve within the current sequence first, then in `globalEntries`.

The thin progress bar starts when the buttons become available. The default duration
is 20 seconds, configured by `OSLinkMessageController.optionalQuestionTimeout`.
Hiding or minimizing the presentation pauses the timer. Its elapsed time is saved
by `sequenceId + entryId`, so restoring the presentation or changing language does
not grant another full countdown. Moving focus to Memory does not pause it.

Expiry completes and closes the question without selecting an option, emitting
`OptionSelected`, applying resources or entering an option reply. The one-shot
consumed receipt remains, preventing the expired question from returning on load.
Messages received while the question is reserved wait in arrival order; they resume
after the question or its selected reply has finished and the message window closes.

An existing explicit `optionTimeoutDelay` with `timeoutOptionId` retains its authored
default-option behavior and uses the same progress bar. This is separate from
unanswered expiry and does not require `followingMessageEntryIds`.

## DOText presentation

While LinkMessage text is typing, the Character window temporarily replaces its
localized title with `ui.os.character.live.typing` and cycles one to three dots.
It may play one configured Character `send` trigger, and continuously picks from
the `Character_00_*` SFX group without immediately repeating the same clip.
Completing, interrupting, minimizing or closing the presentation stops the current
voice clip and restores `ui.os.app.character.name`. Restoring an interrupted entry
starts both `DOText` and these typing effects again.

## Chapter01 Live reconnect

The restart loading callback prepares Live in `WaitClickState` while the reboot
overlay is still covering the OS. After the boot reveal completes,
`chapter01.live_reconnect` becomes the active sequence and starts the interaction
immediately. Only `WaitClickState/TMPRaycast` accepts the normal-entry click. Its
label loops one to three dots, while the red indicator uses the slow pulse. If no
click arrives, the global first concern message is emitted after 5 seconds. Another
5 seconds later `chapter01_live_wait_second` is emitted from the Chapter01 sequence;
it also starts the urgent pulse and shake. Six seconds later the forced path starts
with the stronger shake.

Both the normal and forced paths activate `AnimaState` through the explicit
`UI/OS/CameraSignalIn` horizontal signal effect. This effect is not part of ordinary
state changes. When its 1.6-second shader pass finishes, the shared, currently empty
Chapter01 Animator trigger hook is evaluated. After another 0.3 seconds, the chosen
entry message is presented:

```text
chapter01_live_entry_normal (chapter01.live_reconnect)
  external signal: chapter01.live.entry_normal

chapter01_live_entry_forced (chapter01.live_reconnect)
  external signal: chapter01.live.entry_forced
```

When either entry finishes typing, `chapter01.live.entry_confirm` is queued. Its
three-second trigger delay keeps the completed branch text visible before showing
`chapter01_live_entry_confirm` with `1.确认`. Selecting that sequence option closes
the LinkMessage window, sets Chapter 01, invalidates the selected Memory item, marks
State Check as New, and unlocks Record index 1. Closing the Gift window now only
cleans up that window and icon; it does not advance the chapter.

Opening State Check after that confirmation emits
`chapter01_state_check_memory_damaged`; it auto-closes 5 seconds after typing unless
another message replaces it. Entering Chapter slot 1 emits `chapter01_record_hover`
and starts a hover-only 6-second timer. A real click cancels the timer and presents
`chapter01_record_start_prompt` with `开始修复`. Selecting it continues directly to
`chapter01_find_key`. At 0.3 seconds after that message starts, the runtime opens
`WindowUnit_Special_02` on whichever side of Live has the least overlap with Live
and LinkMessage.

If the pointer stays over the slot for all 6 seconds, the timeout message takes over
the cursor, enlarges and tints it red, and locks normal OS input. After the text
finishes and a 0.3-second reaction pause, the cursor performs a short human-like
check movement, moves to the slot, presses it, and presents the same start prompt.
The existing active-sequence and chapter save fields track the durable state; hover
timing and scripted cursor ownership remain transient.

`WindowUnit_Special_02` contains the non-openable `key_x` desktop process. Its first
pointer entry presents `chapter01_key_hovered`, which remains visible for 5 seconds
after typing unless replaced. Dragging the key onto the desktop acquires it and
closes the special window. Dropping it on a locked Memory or Record process plays
the shared split-lock animation, unlocks that process, places the key in the nearest
available desktop grid slot, and closes the special window. Acquisition is durable:
`chapter01KeyXAcquired` keeps the process available, while the normal desktop layout
record preserves its container and grid index. The key never enters New or Locked
state and clicking it never opens a window.

## Event01 example

```text
Activate sequence: prologue.event01.heart_heal

opening_find_tool_choice
  trigger: OSBecameVisible
  condition: heartHealProcessIndex == 1
  TextWithOptions: "嗯...我想想，我应该备有一份心脏救治的工具......"
  option: "1.等她想起来" -> opening_remembered_folder
  default presentation: DOText

opening_remembered_folder
  direct continuation from the option
  completion cue: restore File and State Check, enable both Live close buttons,
  and play the Folder hint burst

hover_state_check_first / hover_state_check_second
  trigger: DesktopProcessPointerEnter("state_check")
  asset order makes separate pointer entries consume them in order

click_state_check
  trigger: DesktopProcessClicked("state_check")

hover_folder
  trigger: DesktopProcessPointerEnter("file")

hover_live / click_live
  trigger: DesktopProcessPointerEnter/DesktopProcessClicked("character")
  only the DesktopProcessUnit inside Folder participates; taskbar clicks do not

hover_heart_heal_tool
  trigger: DesktopProcessPointerEnter("heart_heal_tool")
  only the DesktopProcessUnit inside Folder participates

heart_heal_recall_choice
  trigger: WindowOpened("WindowUnit_HeartHealTool")
  waits for any active DOText to finish, then branches by the first control
  selected by the player:
  `"<color=#5190B2>电击</color>"` -> heart_heal_shock_first -> heart_heal_start_after_shock
  `"<color=#68C83B>开始</color>"` -> heart_heal_start_first -> heart_heal_shock_after_start

heart_heal_start_after_shock / heart_heal_shock_after_start
  final option text is the plain, unquoted `开始`; unlike the highlighted tutorial
  choices above, it has no rich-text color tag
  final option completion cue: unlock_event01_heart_heal
  restores HeartHealTool input and hides its localized WaitLink presentation

heart_heal_start_hover_ready
  external signal: event01.heart_heal.start_hovered
  first pointer entry on StartButton after the tutorial unlocks

heart_heal_first_hit / heart_heal_first_miss
  external signals from the first resolved BeatUnit hit or natural miss
  first_miss closes automatically 3 seconds after its DOText completes

heart_heal_state_above_65
  external signal when treatment state first crosses from <= 65 to > 65
  closes automatically 5 seconds after its DOText completes

heart_heal_success / heart_heal_fail
  external signals emitted when FinishPart opens its matching action

first_live_reconnect (global)
  trigger: WindowClosed("WindowUnit_Character"), delay 1.2 seconds
  reopen Live over 0.4 seconds, then present the message once per save
  closing Live while it is in VoidMessageState does not emit this trigger
```

After the first treatment result is confirmed, the runtime activates
`prologue.event01.heart_heal_ending`. Existing OS windows are silently closed so
their ProcessUnits are released without emitting story `WindowClosed` triggers,
normal OS input is locked, and the HeartBeatPanel values are temporarily replaced
by `???`. Once Live finishes reopening in its Void state, the
`event01.heart_heal.ending.live_opened` signal starts `heart_heal_ending_fear`, then
`heart_heal_ending_searching`. Its final option enters the immediate, empty
`heart_heal_ending_transition` cue. That cue opens
`OS_SFX/WindowUnit_Special_00`;
once its open animation completes, `heart_heal_ending_key_stuck` is presented.
Selecting `click_me` enters the immediate `heart_heal_ending_cursor_takeover`
cue, which disables physical cursor mapping and animates the virtual cursor red
at 1.5x scale over 0.3 seconds. The resulting `cursor_ready` signal presents
`heart_heal_ending_key_recovered`. After a 1.7-second reading pause, the virtual
cursor uses curved, eased motion and short aim/press/release pauses to select
`take_key` and close LinkMessage. It first drags `Content/Key` horizontally beyond
the left edge of `WindowUnit_Special_00`, pauses as if reacquiring the target, then
curves toward `DesktopProcessUnit_LockHreat`. Once the split lock-open visual completes,
`WindowUnit_Special_00` closes and `WindowUnit_Special_01` opens with a continuously
scrambled five-character TMP using the no-signal glitch. After a reaction pause,
the cursor drags `_01` left and Live right. Half a second later
`event01.heart_heal.ending.windows_arranged` presents
the text-only `heart_heal_ending_check_normal` while the cursor simultaneously starts
a small rightward `_01` drag and three short inspection passes inside the window.
After the text finishes, its trailing dots loop until the inspection completes. A
final 0.5-second pause then emits `inspection_complete`, which presents
`heart_heal_ending_check_result`; after its reading pause the scripted cursor selects
`keep_safe`, closes LinkMessage, closes `_01` after 0.3 seconds, then pauses for 0.2
seconds before moving to
`DesktopProcessUnit_LockHreat`. The split lock visual plays in reverse without a
key, cursor appearance and physical control are restored when the lock closes, and
`event01.heart_heal.ending.relocked` is emitted 0.4 seconds later. That signal
presents the text-only `heart_heal_ending_post_relock`. After its text and a
1.8-second reading pause, `return_ready` presents `heart_heal_ending_return`; its
player-operated `close_os` option closes the OS and clears the ending dim/input state
for the next OS visit.

Until `opening_remembered_folder` completes, Event01 temporarily hides the New
state and darkens/disables only File and State Check. The LinkMessage close button
and Character window close button are darkened and disabled for the same interval.
While this first-treatment branch is incomplete, HeartHealTool shows a 60% dark
raycast blocker and the localized `ui.os.heart_heal.wait_live_reply` label with an
animated ellipsis. Treatment start and shock input are also rejected in code. The
Character and LinkMessage close buttons remain suppressed so an incomplete option
chain cannot strand the tool in its locked state. Escape is consumed while either
Event01 modal interaction is active for the same reason.

All twenty Event01 entries require `heartHealProcessIndex == 1` and are one-shot
per save; the global reconnect message is independently one-shot per save. The six
normal-treatment messages also require the shared recall choice in data, while their
signal producer verifies that one of the two final tutorial entries has completed.
They use `WaitForCurrentTextComplete` so nearby treatment events do not truncate an
active LinkMessage typing animation.

`Event_01` activates the sequence before switching to `OSMain`. `OS_Handle` emits
`OSBecameVisible` only after enabling its presentation. `DesktopProcessUnit` emits a
pointer-enter signal using its stable process key. Consumed entry IDs make both hover
entries one-shot without adding per-object booleans.

## Chapter 01 Tool introduction

`chapter01.memory_repair` becomes active when the unlocked Memory app initializes
during Chapter 01. The departure paragraph's completion signal
`memory.text_completed.1260` presents `chapter01_tool_files_found` and shakes Live.
The entry's consumed state prevents repeats when reopening or relocalizing the
same memory. `OS_Handle.BeginChapter01ToolIntroduction()` remains available for
explicit scripted entry, but opening the first paragraph no longer calls it.

The `start_processing` option advances to `chapter01_tool_processing`. After its
typing finishes, `loopTrailingDots` animates the ending dots for `nextEntryDelay`
(5 seconds), then `nextEntryId` presents `chapter01_tool_await_shortcut`.
`waitForExternalCompletion` keeps this entry incomplete until Ctrl+R. Entries with
`preserveOnOwnerClose` retain their saved presentation when Live closes and suppress
global interruptions while waiting. Reopening Live or restoring the OS rebuilds
the entry; an interrupted processing message retypes and restarts its five-second wait.

Ctrl+R installs Tool, shows its desktop icon and opens the window. The persistent
`OSProcessSaveData.toolUnlocked` flag allows subsequent desktop and shortcut launches.
The first completed boot takes `initializationDuration / initializationSpeed`
(3 / 1.4, about 2.14 seconds), fades `OS Volume_ForStateIndex1.weight`
to 1 and streams 16 batches of four log lines,
then clears the output and resets weight to 0. Closing cancels the boot and
resets the session; minimizing pauses an unfinished boot and preserves input history.
Both minimizing and hiding the OS release the Volume weight immediately.
Completion sets `OSProcessSaveData.toolInitializationCompleted` through GameDataManager.
Later opens immediately show the introduction and input row, without boot logs or
the Volume effect. Cancelling an unfinished first boot does not set the flag.

`OSToolWindowController.InitializationCompleted` fires after the two introduction
lines and command input are ready. OS emits `chapter01.tool.initialized` at this point.
The target `chapter01_tool_ready` comments on the decoded Memory files. Its prerequisite
is the completed Ctrl+R entry. After typing, its trailing dots loop for 6 seconds,
then advance to `chapter01_tool_follow_up`, which prompts `tool unpack --memory`.
Successful unpacking completes that prompt. Early unpacking does not interrupt
the ready message: the prompt completes after its text finishes instead.

`OSToolProgramData.asset` defines the program IDs, localized names, icons and costs:
`gramophone` (0), `energy-loop` (0), `memory-repair-1` (15). Unpacking opens the
text Program Library, using pooled terminal lines for each program's name, energy
cost, installed state and complete `tool install` command. These lines have no
icons, hover action or click-to-fill behavior. Players enter the command manually.
Five seconds without input while the library is visible and focused requests
`chapter01.tool.install_hint` once per
session. The signal waits for the unpack prompt to complete. Its target entry,
`chapter01_tool_install_hint`, deliberately has no text yet and stays pending
without displaying an empty bubble. Fill this entry when the teaching line is ready.

The existing visual library is retained behind `OSToolProgramLibrary.useVisualLibrary`
(off by default) for a later upgrade. In that mode, hovering a row can also request
the hint, and clicking fills its install command without submitting it. Enter starts
`WindowUnit_ToolInstall`, a child window of Tool. Its `AnimationRoot`, progress Image
and `progressChanged` event support a future custom animation. Closing cancels;
minimizing Tool or hiding the OS pauses. Completion rechecks the current battery,
then records the ID in `OSProcessSaveData.installedToolProgramIds` and deducts the
catalog cost once. The other two programs currently record installation only.

Chapter 01's departure section uses narrative nodes 1000 through 1260, with one
paragraph per node. Choices are at 1020, 1060, 1080, 1140, 1170, 1230 and 1260.
Only the maintenance choice at 1170 costs resources: 3 energy. Node 1270 is an
empty continuation entry reserved for the next section. Chapter metadata is retained.
`Tools/OSMemoryData.csv` mirrors the asset for subsequent graph/CSV editing.

Memory typing completion emits `memory.text_completed.<dataIndex>`. Restoring an
already rendered node can recover an unconsumed signal; the Live save state keeps
these messages one-shot. The following entries belong to `chapter01.memory_repair`:

| Node | Entry | Behavior |
| --- | --- | --- |
| 1000 | `chapter01_memory_connected` | Close 4 seconds after Live typing |
| 1050 | `chapter01_memory_strange_humor` | Close 4 seconds after Live typing |
| 1110 | `chapter01_memory_harsh_words` | Close 4 seconds after Live typing |
| 1210 | `chapter01_memory_insects` | Close 4 seconds after Live typing |
| 1260 | `chapter01_tool_files_found` | Shake Live and begin the existing Tool chain |
| 1560 | `chapter01_memory_medicine_shared` | Close 4 seconds after Live typing |
| 1750 | `chapter01_memory_robot_waiting` | Shared comment before the rescue choice at 1760; close 4 seconds after Live typing |
| 1920 | `chapter01_memory_robot_timing` | Close 4 seconds after Live typing |
| 1950 | `chapter01_memory_aidan_early` | Close 4 seconds after Live typing |
| 2040 | `chapter01_memory_mother_daughter_early` | Close 4 seconds after Live typing |
| 2310 | `chapter01_memory_aidan_late` | Close 4 seconds after Live typing |
| 2450 | `chapter01_memory_mother_daughter_late` | Close 4 seconds after Live typing |
| 2570 | `chapter01_memory_robot_assistant` | Close 4 seconds after Live typing |
| 2630 | `chapter01_memory_mother_daughter_recall` | Shared return-home callback; confirmation option grants 5 Heart on selection and closes the message |
| 2670 | `chapter01_memory_upgrade_02` | Close 4 seconds after Live typing |

The street-section comments follow the annotated narrative text. The Aidan and
mother/daughter comments have separate entries for each robot branch (1950/2310
and 2040/2450). These comments use DOText, wait for any current Live text to
finish before replacing it, and remain one-shot. Node 2670 only adds its annotated
comment; the second memory-upgrade Message flow is still a separate step.

All these entries have matching `ui.os.character.link_message.<entryId>` keys
in `LocalizationData.asset`, with all ten project languages populated. Simplified
Chinese matches `fallbackText` exactly; shared lines in the two branches use the
same translations while retaining separate keys.

The robot-waiting comment triggers when node 1750 finishes, before node 1760
offers the rescue/ignore choice. It is shared by both routes and adds no new choice.
The mother/daughter callback triggers at their shared return-home node 2630. Its
line hopes both will recover, since medicine has already been delivered earlier;
it does not imply the girl is still waiting to receive medicine. Its single option,
`reassure_mother_daughter` ("应该没事的。"), has `resourceChange.heart = 5`.
Displaying or restoring the message does not grant resources. Clicking applies the
change, records `sequenceId + entryId + optionId` in the saved
`appliedOptionResourceKeys`, and closes the confirmation. This receipt survives
sequence changes and prevents duplicate rewards after reload or repeated clicks.
Both additions are restricted to Chapter 01 and use saved one-shot entry IDs.

Memory and LinkMessage option translations contain the action text only. Their
resource badges are generated from `resourceChange`, using the Heart/Energy icons
from the status panel (`Resources/OSResourceIcons`) and signed amounts. The robot
power-test tooltip uses the same formatter. TMP sprite glyphs wrap with their
amounts and are centered vertically on the visible text of each line; translated
option rows grow with their content. Cost animations remain separate from these
inline information icons.

The departure option at 1260 contains an action declaration with
`actionId = tool.require_program`, `parameter = memory-repair-1`. This declaration
is a prerequisite, checked before selection and resource changes; it is not emitted
as a selected-option action. A missing program leaves the option visible but blocks
clicks and uses red text/background, with a requirement label above it. Installing
the component refreshes the option immediately. The player then selects departure
to reach 1270; installation never advances the memory automatically. The old
first-line gate and first-line files-found test trigger have been removed.

The terminal supports `help`, `cls`/`clear` and Up/Down history with draft restoration.
`clear` clears output and hides the library; unpacking displays the library again.
Closing resets output, input and history while retaining installed programs. Output pooling
starts at 20 and grows as needed. `CommandSubmitted` exposes submitted nonempty input
for future game commands; this tool does not launch operating-system commands.
Command parsing ignores casing and extra whitespace. Memory's E shortcut only
accepts input when its own window has focus, so terminal typing cannot advance it.

`InputRow/OSToolInputRow` controls only the command row's gray hover Image.
Opening or focusing the window does not select the command input. Clicking the input
enters editing; submission and returning from the installer preserve editing focus.
Its separate `IdleCaret` blinks at one third of `activeCaretBlinkRate` while unfocused;
TMP's native caret uses the full rate while editing (defaults: 2/3 Hz and 2 Hz).
Hover color and active blink rate are serialized for visual tuning. The desktop name,
window TopName and introduction title share `ui.os.app.tool.name`.

### Tool decoration choice

After unpacking completes `chapter01_tool_follow_up`, the library-open signal offers
`chapter01_tool_decoration_prompt`. Each save gets one offer, recorded in
`OSProcessSaveData.toolDecorationState`: None -> Offered -> Declined or Pending -> Completed.
Choosing `decorate_tool` closes the prompt and starts a 2.2-second scan/glyph shader
inside Tool's content area. Input is disabled during this animation. At the reveal,
the title and program rows are enclosed in a star-and-wave ASCII frame; program selection remains manual.
`OSToolLibraryFrame` follows the laid-out rows, resizing its character grid without intercepting input.
The command input and mascot stay outside the frame; text padding keeps wrapped commands clear of its sides.
Each program's second line uses a one-tab-width indent and the localized install-command
label. TMP keeps the label and each install command whole, retaining the indent when wrapping.
Unknown commands, including misspelled `tool` subcommands, report an error; `help`
remains the explicit way to list syntax.

The prompt's optional `optionTimeoutDelay` is 20 seconds, measured after its buttons
become available. `timeoutOptionId` selects `skip_decoration`; the opt-in
`selectTimeoutOptionOnInterrupt` resolves the same choice when another message
replaces it. Neither path offers the choice again. Existing entries without these
fields retain their previous behavior. Minimizing Live or hiding the OS pauses
the choice timer; restoring hidden text follows the existing replay behavior while
retaining the saved elapsed countdown time.

The rebuild pauses when Tool is minimized or the OS is hidden. Closing mid-rebuild
keeps Pending, so the next unpack resumes the accepted work. Completing it records
Completed before emitting `chapter01.tool.decoration_done`. Its text closes five
seconds after typing finishes. `ToolMascot` uses a reserved footer below the scroll
viewport, aligned left. After an eight-second idle interval, its kaomoji winks and sends
a star drifting right and fading out before returning to idle.
`OSToolDecorationController` exposes duration, footer height, mascot timing and star travel;
`M_OSToolRebuild` controls the transition tint. The existing icon library remains off.

Tool's input row shows a faint idle background and the localized `ui.os.tool.input.placeholder`
when empty and unfocused. Hover strengthens the background; focusing hides the placeholder
and keeps the active background even after the pointer leaves. Only TMP's editing caret blinks.
Switching windows preserves the command draft and hides the caret. Closing Tool still resets the session.

## Save contract

`chapter02.recovery` also includes `chapter02_departure_busy`: after repair
confirmation, the first `state_check` pointer-enter in Chapter 2 shows an immediate
text-only message and plays Expend02. The chapter director completes this protected
entry three seconds after text appears, then reverses the existing camera signal
shader into No Signal without OpenLive. Its completion receipt restores the offline
state; global messages, including asynchronous reconnect completion, yield while
Live is away.

At the completed 4310 upgrade prompt, the director reconnects with the same signal-in
shader and OpenLive appearance, then presents `chapter02_return_arrived` (Expend02,
Wait option). Wait advances to `chapter02_return_working` (Expend01, immediate text,
looping trailing dots, external completion after four seconds). Completion closes
Tool if open, marks its desktop icon NEW, restores ordinary idle and presents
`chapter02_return_ready` with the No option. No closes the message. All three entries
preserve their presentation on owner close and use the existing sequence receipts.
The actual third upgrade installation and JRPG are deferred.

```text
activeSequenceId
consumedEntryIds[]
completedEntryIds[]       // distinguishes DOText completion from interruption
appliedOptionResourceKeys[] // one resource receipt per sequence / entry / option
presentingEntryId
pendingEntryId            // delayed/replacement entries and unwritten placeholders
phase                     // inactive / waiting / presenting / completed
isCompleted
consumedGlobalEntryIds[]
presentingGlobalEntryId
pendingGlobalEntryId
optionCountdownKey        // sequenceId / entryId owning the saved timer
optionCountdownElapsed    // elapsed visible time after buttons became available
optionCountdownQueue[]    // FIFO records containing sequenceId + entryId
```

Loading restores the presenting entry and all independently consumed entries.
Presentation is rebuilt from the start of the saved entry; one-shot effects must
distinguish first entry from resume so they do not replay unintentionally. If an ID is
renamed or removed, add save migration rather than relying on the entry list index.

## Extension rules

- New Part: add a content data type and matching Presenter.
- New trigger: add a semantic signal producer and trigger matcher.
- New flow behavior: represent it in SO data and the Runner.
- New visual-only behavior: add a presentation handler; do not put it in save logic.
