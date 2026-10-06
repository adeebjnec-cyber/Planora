# Planora - Handover

## What to build
**Planora** is project scheduling and management software that matches Microsoft Project feature for feature. It is a final-year project by Adeeb and is being proposed to a professor (see `Planora_Project_Proposal.pdf`).

## Hard constraints
- **Native desktop application.** Not Electron. Not web. No browser-based UI.
- Works fully offline.
- **Single phase.** Every feature below is in scope now. Do not defer anything to "later phases".
- Free and open source.

## Open decision (ask the user first)
- Language and UI framework are **not chosen yet**. Ask Adeeb before writing code. Options that fit the constraints include C# with WPF or WinUI, Qt with C++, or Qt with Python. Also confirm the target OS (Windows assumed, since Microsoft Project is the reference).

## Features (all required)

### Task management
- Task table, hierarchical WBS with summary tasks, milestones, recurring tasks
- Duration, start/finish dates, percent complete, notes
- Outline indent/outdent

### Dependencies
- Finish-to-Start, Start-to-Start, Finish-to-Finish, Start-to-Finish
- Lead and lag
- Task constraints: ASAP, ALAP, Must Start On, Start No Earlier Than, etc.
- Deadlines

### Scheduling engine
- Forward and backward pass, early/late dates
- Total float and free float
- Automatic critical path
- Auto-scheduled and manually scheduled modes
- Automatic rescheduling when tasks change

### Gantt chart
- Timeline bars, dependency arrows, critical path highlighting, progress bars
- Zoom levels, drag-to-edit, split tasks, today line

### Resources
- Work, material and cost resources
- Assignments, units and rates
- Over-allocation detection and resource leveling
- Resource usage and task usage views

### Calendars
- Project, task and resource calendars
- Working days and hours, holidays and exceptions

### Baselines and tracking
- Multiple baselines
- Planned vs actual, variance, progress lines, status date

### Cost management
- Fixed and resource costs, budgets, cost roll-up, cost variance

### Views
- Gantt chart, network diagram, calendar view, task board, timeline, resource sheet

### Filtering and grouping
- Filters, sorting, grouping, custom fields and columns, saved views

### Reports
- Project summary, critical tasks, cost, resource and progress reports

### Import / export
- Microsoft Project XML, Excel/CSV, PDF and image export, printing

### Editing tools
- Undo/redo, copy/paste of tasks, templates, keyboard shortcuts, autosave

## Definition of done
Planora matches Microsoft Project on every feature listed above. The comparison table in the proposal marks all of them "Yes" for Planora. The only intended differences are:

| | Microsoft Project | Planora |
|---|---|---|
| Platform | Windows desktop | Native desktop |
| Source code | Closed | Open |
| Cost | Paid licence / subscription | Free |

## Suggested approach (suggestion, not a requirement)
1. Keep the scheduling engine as a separate module with no UI dependencies, with unit tests (CPM examples, calendars, constraints).
2. Use a simple JSON file format for projects, plus the import/export formats above.
3. Check engine results against known Microsoft Project output.
4. Build the UI on top of the engine.

## Notes
- The proposal PDF has only three parts: cover, features table, comparison table. It has no paragraphs and no UI mockups.
- Server-side Microsoft products (Project Server, Microsoft 365 / Teams integration) are deliberately excluded.
