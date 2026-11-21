# DeckIsland settings: design system

Product: DeckIsland is a Windows desktop app: a small "island" panel at the top of the screen (music, weather, system monitor, widgets). This design is its Settings window, a native desktop window, not a website. Fixed window about 1168x972, dark by default.

Layout: left sidebar 230px (logo, grouped nav: Basics / AI usage / Music; the active item is a rounded filled row with a 3px blue bar on its left). Right: scrolling content column with 46px side padding, page title 28px semibold, "Restore defaults" quiet text button top right.

Colours (dark): window #141416, sidebar #161618, raised fill rgba(255,255,255,.06), selected fill rgba(255,255,255,.12), hairline rgba(255,255,255,.09), text primary #F5F5F7, text secondary rgba(245,245,247,.62), accent blue #2F80ED (switch on, slider fill, focus ring), danger #FF5A52.
Type: Segoe UI Variable, 13px body, 12px captions, 15px card titles, 28px page title. Tabular numerals for numbers.
Radii: controls 8px, cards 14px, pills fully round. No shadows; separation by fill and hairline only. Motion: 120ms ease for hover/press.

Controls (all already exist in the app): switch (40x24, grey off, blue on); segmented control (2-3 options, selected segment has a lighter fill and a hairline); select (dark input with chevron, 36px high); number input with small +/- steppers; slider (4px track, round 16px thumb, blue fill); quiet text button; info "i" icon with tooltip.

Page to design: Settings, General. Settings in it, in order:
1. Startup and resources: Start with Windows (switch); Show tray icon (switch, info); Resource use (segmented: Saver / Balanced / Smooth, info); Exit DeckIsland (action).
2. Appearance and language: Theme (segmented: System / Light / Dark); Language (select, English); Scale (select, 100%); Corners (slider 0-100 with a number field); Background transparency (slider 0-100 with a number field); Interface font (select, Default, info); Apply font to settings window (switch); Font preview (sample text line).
