import { useState, useRef, useEffect, useCallback } from 'react'

// ─── Types ──────────────────────────────────────────────────────────────────

interface GuideSection {
  id: string
  title: string
  summary: string
  content: React.ReactNode
}

// ─── Callout components ─────────────────────────────────────────────────────

function InfoCallout({ children }: { children: React.ReactNode }) {
  return (
    <div className="my-3 px-3 py-2.5 bg-blue-500/[0.07] border border-blue-400/20 rounded-lg">
      <div className="flex items-start gap-2">
        <svg className="w-3.5 h-3.5 text-blue-400 shrink-0 mt-0.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
        </svg>
        <div className="text-[10px] text-blue-300/80 leading-relaxed">{children}</div>
      </div>
    </div>
  )
}

function WarnCallout({ children }: { children: React.ReactNode }) {
  return (
    <div className="my-3 px-3 py-2.5 bg-amber-500/[0.07] border border-amber-400/20 rounded-lg">
      <div className="flex items-start gap-2">
        <svg className="w-3.5 h-3.5 text-amber-400 shrink-0 mt-0.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4.5c-.77-.833-2.694-.833-3.464 0L3.34 16.5c-.77.833.192 2.5 1.732 2.5z" />
        </svg>
        <div className="text-[10px] text-amber-300/80 leading-relaxed">{children}</div>
      </div>
    </div>
  )
}

function CodeBlock({ children }: { children: string }) {
  return (
    <pre className="my-2 px-3 py-2 bg-[#1a1a2e] border border-fn-border rounded-lg text-[10px] font-mono text-gray-300 overflow-x-auto whitespace-pre-wrap">
      {children}
    </pre>
  )
}

function SeverityBadge({ level }: { level: 'critical' | 'medium' | 'low' }) {
  const styles = {
    critical: 'bg-red-500/20 text-red-300 border-red-500/30',
    medium: 'bg-amber-500/20 text-amber-300 border-amber-500/30',
    low: 'bg-gray-500/20 text-gray-400 border-gray-500/30',
  }
  const labels = { critical: 'Critical', medium: 'Medium', low: 'Low' }
  return (
    <span className={`inline-block px-1.5 py-0.5 text-[9px] font-semibold uppercase rounded border ${styles[level]}`}>
      {labels[level]}
    </span>
  )
}

function StepList({ steps }: { steps: string[] }) {
  return (
    <ol className="my-2 space-y-1.5 pl-4">
      {steps.map((step, i) => (
        <li key={i} className="text-[11px] text-gray-400 leading-relaxed list-decimal">
          {step}
        </li>
      ))}
    </ol>
  )
}

function BulletList({ items }: { items: string[] }) {
  return (
    <ul className="my-2 space-y-1 pl-4">
      {items.map((item, i) => (
        <li key={i} className="text-[11px] text-gray-400 leading-relaxed list-disc">
          {item}
        </li>
      ))}
    </ul>
  )
}

// ─── Chevron icon ───────────────────────────────────────────────────────────

function ChevronIcon({ expanded }: { expanded: boolean }) {
  return (
    <svg
      className={`w-3.5 h-3.5 text-gray-500 shrink-0 transition-transform duration-200 ${expanded ? 'rotate-90' : ''}`}
      fill="none"
      viewBox="0 0 24 24"
      stroke="currentColor"
      strokeWidth={2}
    >
      <path d="M9 5l7 7-7 7" />
    </svg>
  )
}

// ─── Section definitions ────────────────────────────────────────────────────

const sections: GuideSection[] = [
  {
    id: 'getting-started',
    title: 'Getting Started',
    summary: 'What WellVersed does and how to set up your first project.',
    content: (
      <>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-3">
          WellVersed is a companion studio for Unreal Editor for Fortnite (UEFN). It reads your project files
          to give you deep insight into devices, widgets, verse code, and level structure -- then lets you
          generate, edit, and audit assets without leaving the app. It runs safely alongside UEFN with your map open.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mb-1.5">Quick Setup</h4>
        <StepList steps={[
          'Launch WellVersed -- the app starts the .NET sidecar automatically.',
          'Go to Projects (sidebar) and click "Add Project".',
          'Browse to your UEFN project folder (the one containing a .uefnproject file).',
          'Choose "My Project" tier -- this enables full editing with safety protections.',
          'Your project appears in the sidebar. Select a level to start exploring.',
        ]} />
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">First Project</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed">
          After adding your project, click it in the sidebar dropdown. The Dashboard page gives you an instant
          overview: asset counts, device breakdown, verse file summary, and health score. From there, navigate
          to Levels to see your maps, then pick a level to inspect its devices.
        </p>
        <InfoCallout>
          All file reads use copy-on-read -- WellVersed never locks your source files.
          If UEFN is running, edits go to a staging directory for you to review first.
        </InfoCallout>
      </>
    ),
  },
  {
    id: 'python-bridge',
    title: 'Python Bridge Setup',
    summary: 'Connect WellVersed to UEFN via the Python Editor Scripting bridge.',
    content: (
      <>
        <h4 className="text-[11px] font-semibold text-gray-200 mb-1.5">Enable Python Editor Scripting in UEFN</h4>
        <StepList steps={[
          'Open UEFN and go to Edit > Editor Preferences.',
          'Search for "Python" in the preferences search bar.',
          'Under Plugins > Python Editor Scripting, check "Enable Python Editor Scripting".',
          'Restart UEFN to activate the Python plugin.',
        ]} />
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Install Bridge Files</h4>
        <StepList steps={[
          'Locate the bridge files in the WellVersed install (bridge/ folder).',
          'Copy all .py files to your project\'s Content/Python/ directory.',
          'If the Python/ folder doesn\'t exist, create it.',
          'Restart UEFN -- the bridge script auto-starts on load.',
        ]} />
        <CodeBlock>{`YourProject/
  Content/
    Python/
      wellversed_bridge.py     # Main bridge server
      wellversed_commands.py   # 114 bridge commands
      wellversed_startup.py    # Auto-start hook`}</CodeBlock>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Connection Status</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          When the bridge is connected, a green bar appears at the top of the app showing "Bridge connected".
          The bridge enables live commands: spawning actors, reading viewport state, executing Verse builds, and more.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Online vs Offline</h4>
        <BulletList items={[
          'Online (bridge connected): Live UEFN control -- spawn actors, move objects, trigger builds, read viewport.',
          'Offline (no bridge): Full read/write of project files, auditing, widget editing, verse tools. Most features work without the bridge.',
        ]} />
        <WarnCallout>
          The Python bridge is experimental and has not been tested with a real UEFN session.
          File-based features (reading assets, editing widgets, auditing) work independently of the bridge.
        </WarnCallout>
      </>
    ),
  },
  {
    id: 'studio',
    title: 'Studio (AI Generation)',
    summary: 'Describe what you want and let the AI generation pipeline build it.',
    content: (
      <>
        <h4 className="text-[11px] font-semibold text-gray-200 mb-1.5">How the Chat Works</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          Studio is a chat-based interface where you describe what you want to build. Type a natural language
          description and the pipeline analyzes your intent, plans the steps, and executes them using the
          project's actual asset data.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Quick Actions</h4>
        <BulletList items={[
          'Generate Verse -- creates a Verse device file from a description.',
          'Create Widget -- builds a UMG widget blueprint from a layout description.',
          'Explain Device -- describes how a selected device works and its configuration.',
          'Game Loop -- generates the DFA state machine for a game mode description.',
        ]} />
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Generation Pipeline</h4>
        <StepList steps={[
          'Describe: You tell Studio what you want in plain language.',
          'Plan: The system analyzes your request, identifies which tools and assets are involved.',
          'Execute: Steps run sequentially -- you see progress as each completes.',
          'Review: Results appear inline. Generated files go to staging for review before applying.',
        ]} />
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Tips for Good Descriptions</h4>
        <BulletList items={[
          'Be specific about device types: "a vending machine that costs 500 gold" not "a shop".',
          'Reference existing assets by name when possible: "use the RedTeamSpawner device".',
          'For widgets, describe layout structure: "a horizontal bar with 3 icons and a score label on the right".',
          'For verse, state the trigger and effect: "when a player enters the zone, start a 30-second timer".',
        ]} />
        <InfoCallout>
          Studio currently uses smart template matching for generation. Full AI model integration is planned.
          Results are good for common patterns but may need manual refinement for complex logic.
        </InfoCallout>
      </>
    ),
  },
  {
    id: 'widget-editor',
    title: 'Widget Editor',
    summary: 'Visual editor for UMG widget blueprints with .uasset export.',
    content: (
      <>
        <h4 className="text-[11px] font-semibold text-gray-200 mb-1.5">Loading Widgets</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          Open the Widget Editor from the sidebar. You can load an existing .uasset widget blueprint from your
          project, or start with a blank canvas. The editor parses the binary .uasset format and reconstructs
          the widget tree visually.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Canvas Editing</h4>
        <BulletList items={[
          'Click any widget on the canvas to select it -- properties appear in the right panel.',
          'Drag widgets to reposition them (Canvas Panel children only).',
          'Use the hierarchy tree on the left to navigate complex widget structures.',
          'Right-click a widget to add children, delete, or duplicate.',
          'Edit text, colors, fonts, and layout properties in the property inspector.',
        ]} />
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Export</h4>
        <BulletList items={[
          'Export .uasset -- writes the widget back to binary format, compatible with UEFN.',
          'Generate Verse -- creates the Verse class that corresponds to this widget (bindings, event handlers).',
        ]} />
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Containment Rules</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed">
          UEFN widgets have strict parent-child rules. For example, a Canvas Panel can hold any widget but
          an Overlay can only hold certain slot types. The editor enforces these rules -- if you try to add
          an invalid child, it will warn you. These rules were derived from analyzing 883 real UEFN widgets.
        </p>
        <InfoCallout>
          Widget roundtrip fidelity is approximately 80%. Some advanced properties (custom materials,
          complex bindings) may not survive a load-edit-save cycle. Always keep backups.
        </InfoCallout>
      </>
    ),
  },
  {
    id: 'device-tools',
    title: 'Device Tools',
    summary: 'Inspect devices, view wiring graphs, and explore the encyclopedia.',
    content: (
      <>
        <h4 className="text-[11px] font-semibold text-gray-200 mb-1.5">Device Inspector</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          Select a level, then open Devices. WellVersed reads every external actor file and parses all
          non-default properties. Devices are grouped by type with property importance ranking -- meaningful
          configuration (team index, costs, timers) appears first, rendering noise (LOD, collision) is collapsed.
          Over 130 properties are parseable per device thanks to our UAssetAPI fix.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Device Wiring Graph</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          The wiring graph visualizes how devices are connected through event channels. Nodes represent
          devices, edges represent event-to-action bindings. This shows you the logical flow of your
          map at a glance -- which devices trigger which, and through what events.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Blueprint Graph</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          A node-based view of blueprint logic extracted from your assets. Renders execution flow,
          variable reads/writes, and function calls as connected nodes similar to the UE blueprint editor.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Device Encyclopedia</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed">
          Searchable reference of all UEFN device types. Browse device categories, see every configurable
          property with descriptions and default values, and view example configurations from real maps.
          Sourced from .digest schema files that define the Verse device class hierarchy.
        </p>
      </>
    ),
  },
  {
    id: 'dfa-simulator',
    title: 'DFA Simulator',
    summary: 'Model game state machines and step through event-driven logic.',
    content: (
      <>
        <h4 className="text-[11px] font-semibold text-gray-200 mb-1.5">What It Models</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          The DFA (Deterministic Finite Automaton) Simulator models your map's game loop as a state machine.
          States represent game phases (lobby, warmup, round, overtime, game-over), and transitions represent
          events that move between phases. This helps you verify your game logic before testing in UEFN.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Reading the Graph</h4>
        <BulletList items={[
          'Circles are states -- the current state is highlighted.',
          'Arrows are transitions -- labeled with the triggering event.',
          'Green border = initial state. Double border = accepting (end) state.',
          'Hover over a transition to see its guard conditions if any.',
        ]} />
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Stepping Through</h4>
        <StepList steps={[
          'The simulator starts at the initial state.',
          'Available events appear as buttons below the graph.',
          'Click an event to fire it -- the state machine transitions.',
          'The history panel on the right shows every step taken.',
          'Use Reset to return to the initial state.',
        ]} />
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Custom Events</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed">
          You can type any event name to fire it manually. If the current state has a transition for that
          event, the machine will move. If not, the simulator shows "no transition" -- useful for testing
          that your state machine rejects invalid sequences.
        </p>
      </>
    ),
  },
  {
    id: 'qa-audit',
    title: 'QA & Audit',
    summary: 'Run project audits, view health scores, and check publish readiness.',
    content: (
      <>
        <h4 className="text-[11px] font-semibold text-gray-200 mb-1.5">Running Audits</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          The Audit page runs a comprehensive scan of your project. It checks every level, device, verse
          file, and asset for common issues. Results are categorized by severity (Error, Warning, Info) and
          grouped by area. Click "Run Audit" to start -- large projects may take a few seconds.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Health Scores</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          The Health Report page computes a letter grade (A+ through F) based on audit findings. Scoring:
        </p>
        <BulletList items={[
          'A+ (95-100): No significant issues found.',
          'A/A- (85-94): Minor warnings only.',
          'B range (70-84): Some recommended fixes.',
          'C range (55-69): Notable issues that should be addressed.',
          'D (50-54): Significant problems.',
          'F (below 50): Critical issues blocking publish.',
        ]} />
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">10 Analysis Passes</h4>
        <StepList steps={[
          'Asset integrity -- checks all .uasset files parse without errors.',
          'Device configuration -- validates property values against digest schemas.',
          'Wiring consistency -- ensures event channels have both senders and receivers.',
          'Verse compilation -- checks for syntax errors and unresolved references.',
          'Level structure -- validates external actor paths and level metadata.',
          'Widget validity -- checks widget trees for containment rule violations.',
          'Resource references -- finds broken or missing asset references.',
          'Naming conventions -- flags non-standard asset names.',
          'Performance hints -- warns about excessive actors or complex hierarchies.',
          'Publish readiness -- checks Epic-required metadata and settings.',
        ]} />
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Publish Checklist</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed">
          The Publish page provides a focused checklist of requirements for publishing your map.
          Items are marked required, recommended, or optional. Some issues are auto-fixable -- click
          the fix button to resolve them. The page computes a publish readiness grade.
        </p>
      </>
    ),
  },
  {
    id: 'verse-tools',
    title: 'Verse Tools',
    summary: 'Browse, edit, explain errors, and generate verse code.',
    content: (
      <>
        <h4 className="text-[11px] font-semibold text-gray-200 mb-1.5">File Browser & Editor</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          The Verse Files page shows all .verse files in your project. Click a file to open it in the
          built-in editor with syntax highlighting. Edits go through the staging system -- you review
          changes before they're applied to your project.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Error Explainer</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          The Error Explainer recognizes 45 common Verse compiler error patterns. Paste an error message
          and it identifies the issue, explains what went wrong in plain language, and suggests a fix.
          Patterns cover type mismatches, missing imports, invalid syntax, concurrency issues, and more.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Smart Templates</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          20 pre-built Verse templates for common patterns:
        </p>
        <div className="grid grid-cols-2 gap-x-4 gap-y-0.5 my-2">
          {[
            'Timer Device', 'Score Manager', 'Team Assigner', 'Zone Trigger',
            'Elimination Tracker', 'Round Manager', 'Spawn Controller', 'Item Granter',
            'UI Manager', 'Leaderboard', 'Phase Controller', 'Map Teleporter',
            'Inventory System', 'Objective Tracker', 'Wave Spawner', 'Power-Up System',
            'Voting System', 'Loot Table', 'Damage Zone', 'Custom Event Bus',
          ].map((t) => (
            <span key={t} className="text-[10px] text-gray-500">{t}</span>
          ))}
        </div>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Custom Generation</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed">
          Use Studio (chat) to generate custom Verse devices from descriptions. The generator understands
          UEFN's Verse dialect including creative_devices, events, concurrency, and the module system.
        </p>
      </>
    ),
  },
  {
    id: 'project-management',
    title: 'Project Management',
    summary: 'Snapshots, diffs, file watching, staged changes, and stamps.',
    content: (
      <>
        <h4 className="text-[11px] font-semibold text-gray-200 mb-1.5">Snapshots & Diffs</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          WellVersed can snapshot your project state and compare snapshots over time. The Project Diff
          page shows which files changed, what properties were modified, and when. Useful for tracking
          changes between editing sessions or comparing before/after a big modification.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">File Watcher</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          When a project is active, WellVersed monitors the project directory for changes. If files are
          modified externally (e.g., by UEFN), a notification bar appears at the top of the app. This
          keeps the dashboard data fresh and warns about concurrent edits.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Staged Changes</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          All edits are queued as pending changes. The Staged Changes page shows every modification waiting
          to be applied. You can review diffs, approve individual changes, discard them, or apply all at once.
          When UEFN is running, writes always go to staging (.wellversed/staged/) rather than directly to your project.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Stamps</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed">
          Stamps let you save groups of actors (devices, props, placements) as reusable templates. Save a
          stamp from your level, then place it again later -- positions, rotations, and property overrides
          are all preserved. Useful for duplicating room layouts, arena setups, or device clusters.
        </p>
      </>
    ),
  },
  {
    id: 'reference-library',
    title: 'Reference Library',
    summary: 'Browse and search across multiple UEFN projects for reference.',
    content: (
      <>
        <h4 className="text-[11px] font-semibold text-gray-200 mb-1.5">Adding Libraries</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          Libraries are UEFN project directories you add as read-only references. Go to the Library section
          in the sidebar and click "Add Library". Browse to any UEFN project folder -- it's added as read-only.
          You can add as many libraries as you want. Build the index to enable deep search features.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Searching Across Projects</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          Once indexed, you can search across all library projects for specific asset types, device
          configurations, verse patterns, widget structures, and materials. The Asset Search page lets
          you query by name, type, or property value across your entire reference collection.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">System Extraction & Import</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed">
          Find a device configuration or verse pattern you like in a library? Extract it as a reference
          to understand how it works. Cross-map asset import is planned -- simple for Epic-only references,
          more complex when user assets have dependencies.
        </p>
        <InfoCallout>
          Libraries are always read-only. WellVersed never modifies reference project files.
          The sandbox at Z:\UEFN_Resources contains ~92 UEFN projects used as a reference collection.
        </InfoCallout>
      </>
    ),
  },
  {
    id: 'claude-mcp',
    title: 'Claude Code MCP',
    summary: 'Use WellVersed as an MCP server for Claude Code with 90+ tools.',
    content: (
      <>
        <h4 className="text-[11px] font-semibold text-gray-200 mb-1.5">Overview</h4>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-2">
          WellVersed includes an MCP (Model Context Protocol) server that exposes 90+ tools to Claude Code.
          This lets you use natural language in your terminal to read, audit, modify, and generate UEFN
          project assets. The MCP server runs as a sidecar process communicating over stdio.
        </p>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Key Commands to Try</h4>
        <CodeBlock>{`# Start the MCP server
dotnet run --project src/FortniteForge.MCP -- path/to/forge.config.json

# In Claude Code, try:
"List all devices in my level"
"Show me the vending machine configuration"
"What verse files are in my project?"
"Run an audit and tell me what to fix"
"Generate a timer device that resets every 30 seconds"
"Show me all broken wiring connections"`}</CodeBlock>
        <h4 className="text-[11px] font-semibold text-gray-200 mt-3 mb-1.5">Example Workflow</h4>
        <StepList steps={[
          'Start the MCP server pointing at your project config.',
          'Ask Claude Code to audit your project -- it uses the audit tools to scan for issues.',
          'Ask it to explain a specific device -- it reads the .uasset and describes every property.',
          'Ask it to generate verse code -- it uses project context to create accurate device scripts.',
          'Review generated files in the Staged Changes page before applying.',
        ]} />
        <InfoCallout>
          The MCP server respects the same safety model as the app. Read-only projects stay read-only.
          All writes go through the staging system.
        </InfoCallout>
      </>
    ),
  },
  {
    id: 'known-issues',
    title: 'Known Issues',
    summary: 'Current limitations and their severity.',
    content: (
      <>
        <p className="text-[11px] text-gray-400 leading-relaxed mb-3">
          WellVersed is under active development. These are the known limitations:
        </p>
        <div className="space-y-3">
          <div className="flex items-start gap-2.5">
            <SeverityBadge level="critical" />
            <div>
              <div className="text-[11px] text-gray-300 font-medium">Python bridge untested with real UEFN</div>
              <div className="text-[10px] text-gray-500 mt-0.5">
                The 114 bridge commands are implemented but have not been validated against a live UEFN session.
                File-based features work independently.
              </div>
            </div>
          </div>
          <div className="flex items-start gap-2.5">
            <SeverityBadge level="medium" />
            <div>
              <div className="text-[11px] text-gray-300 font-medium">Widget roundtrip ~80% fidelity</div>
              <div className="text-[10px] text-gray-500 mt-0.5">
                Loading a widget .uasset, editing it, and saving back preserves most properties but some
                advanced features (custom materials, complex bindings) may be lost. Always keep backups.
              </div>
            </div>
          </div>
          <div className="flex items-start gap-2.5">
            <SeverityBadge level="medium" />
            <div>
              <div className="text-[11px] text-gray-300 font-medium">ChatPage uses templates, not real AI</div>
              <div className="text-[10px] text-gray-500 mt-0.5">
                The Studio chat currently uses smart template matching for code generation.
                Full AI model integration (Claude API) is planned but not yet connected.
              </div>
            </div>
          </div>
          <div className="flex items-start gap-2.5">
            <SeverityBadge level="low" />
            <div>
              <div className="text-[11px] text-gray-300 font-medium">Widget anchors hardcoded to TopLeft</div>
              <div className="text-[10px] text-gray-500 mt-0.5">
                The widget editor currently sets all anchors to TopLeft (0,0). Custom anchor points
                (center, stretch, etc.) are not yet supported in the visual editor.
              </div>
            </div>
          </div>
          <div className="flex items-start gap-2.5">
            <SeverityBadge level="low" />
            <div>
              <div className="text-[11px] text-gray-300 font-medium">Smart templates uncompiled</div>
              <div className="text-[10px] text-gray-500 mt-0.5">
                The 20 Verse smart templates generate syntactically correct code but have not been
                compiled against the UEFN Verse compiler. Minor adjustments may be needed.
              </div>
            </div>
          </div>
        </div>
        <WarnCallout>
          Always keep backups of your project before applying any modifications.
          WellVersed creates automatic backups for direct writes, but staging-based edits
          require your explicit approval before touching source files.
        </WarnCallout>
      </>
    ),
  },
]

// ─── Main component ─────────────────────────────────────────────────────────

export function GuidePage() {
  const [expandedSections, setExpandedSections] = useState<Set<string>>(() => new Set([sections[0].id]))
  const [activeTocId, setActiveTocId] = useState<string>(sections[0].id)
  const contentRef = useRef<HTMLDivElement>(null)
  const sectionRefs = useRef<Map<string, HTMLDivElement>>(new Map())

  const toggleSection = useCallback((id: string) => {
    setExpandedSections((prev) => {
      const next = new Set(prev)
      if (next.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }
      return next
    })
  }, [])

  const scrollToSection = useCallback((id: string) => {
    // Expand the section if collapsed
    setExpandedSections((prev) => {
      const next = new Set(prev)
      next.add(id)
      return next
    })
    // Scroll after a tick to allow expansion
    requestAnimationFrame(() => {
      const el = sectionRefs.current.get(id)
      if (el && contentRef.current) {
        contentRef.current.scrollTo({
          top: el.offsetTop - contentRef.current.offsetTop - 16,
          behavior: 'smooth',
        })
      }
    })
  }, [])

  // Track which section is in view for TOC highlighting
  useEffect(() => {
    const container = contentRef.current
    if (!container) return

    const handleScroll = () => {
      const scrollTop = container.scrollTop + container.offsetTop + 60
      let currentId = sections[0].id
      for (const section of sections) {
        const el = sectionRefs.current.get(section.id)
        if (el && el.offsetTop <= scrollTop) {
          currentId = section.id
        }
      }
      setActiveTocId(currentId)
    }

    container.addEventListener('scroll', handleScroll, { passive: true })
    return () => container.removeEventListener('scroll', handleScroll)
  }, [])

  return (
    <div className="flex-1 flex min-h-0 bg-fn-darker">
      {/* Left TOC sidebar */}
      <div className="w-[200px] shrink-0 border-r border-fn-border bg-fn-dark overflow-y-auto py-4 px-2">
        <div className="px-2 mb-4">
          <h2 className="text-[13px] font-semibold text-white tracking-wider uppercase">User Guide</h2>
          <p className="text-[9px] text-gray-600 mt-1">WellVersed v1.0</p>
        </div>
        <nav className="space-y-0.5">
          {sections.map((section) => (
            <button
              key={section.id}
              className={`w-full text-left px-2 py-1.5 rounded transition-colors ${
                activeTocId === section.id
                  ? 'text-fn-rare bg-fn-rare/10'
                  : 'text-gray-500 hover:text-gray-300 hover:bg-white/[0.03]'
              }`}
              onClick={() => scrollToSection(section.id)}
            >
              <span className="text-[10px] font-medium leading-tight block">{section.title}</span>
            </button>
          ))}
        </nav>
      </div>

      {/* Main content area */}
      <div ref={contentRef} className="flex-1 overflow-y-auto py-6 px-8">
        <div className="max-w-[700px] mx-auto">
          {/* Page header */}
          <div className="mb-8">
            <h1 className="text-[18px] font-semibold text-white mb-2">WellVersed User Guide</h1>
            <p className="text-[11px] text-gray-500 leading-relaxed">
              Everything you need to know about using WellVersed to manage, audit, and generate
              assets for your UEFN projects. Click a section to expand it, or use the table of
              contents on the left to jump to a topic.
            </p>
          </div>

          {/* Sections */}
          <div className="space-y-2">
            {sections.map((section) => {
              const isExpanded = expandedSections.has(section.id)
              return (
                <div
                  key={section.id}
                  ref={(el) => {
                    if (el) sectionRefs.current.set(section.id, el)
                  }}
                  className="bg-fn-panel border border-fn-border rounded-lg overflow-hidden"
                >
                  {/* Section header */}
                  <button
                    className="w-full flex items-center gap-3 px-4 py-3 hover:bg-white/[0.02] transition-colors text-left"
                    onClick={() => toggleSection(section.id)}
                  >
                    <ChevronIcon expanded={isExpanded} />
                    <div className="flex-1 min-w-0">
                      <h3 className="text-[13px] font-semibold text-white tracking-wider uppercase">
                        {section.title}
                      </h3>
                      {!isExpanded && (
                        <p className="text-[10px] text-gray-500 mt-0.5 truncate">{section.summary}</p>
                      )}
                    </div>
                  </button>

                  {/* Section content */}
                  {isExpanded && (
                    <div className="px-4 pb-4 pt-1 border-t border-fn-border/50">
                      {section.content}
                    </div>
                  )}
                </div>
              )
            })}
          </div>

          {/* Footer */}
          <div className="mt-8 pt-4 border-t border-fn-border/50 text-center">
            <p className="text-[10px] text-gray-600">
              WellVersed -- A UEFN project management studio.
            </p>
          </div>
        </div>
      </div>
    </div>
  )
}
