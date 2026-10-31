import { useMemo, useState, type KeyboardEvent } from 'react';
import { Braces, Check, ChevronDown, ChevronsDown, ChevronsUp, Code2, GitBranch, Maximize, Moon, PanelLeftClose, PanelLeftOpen, Play, RotateCcw, Square, Sun, TriangleAlert } from 'lucide-react';
import AstCanvas, { indexAst } from './AstCanvas';
import CodeEditor from './CodeEditor';
import Inspector from './Inspector';
import { examples } from './examples';
import { cancelExecution, executeSource, parseRoslyn, parseSource } from './api';
import type { AstNodeDto, InspectorSelection, ParseSuccess } from './types';

type Mode = 'run' | 'roslyn' | 'education';
export default function App() {
  const [mode, setMode] = useState<Mode>('run');
  const [source, setSource] = useState(examples[0].code);
  const [exampleIndex, setExampleIndex] = useState(0);
  const [ast, setAst] = useState<AstNodeDto | null>(null);
  const [nodeCount, setNodeCount] = useState(0);
  const [tokenCount, setTokenCount] = useState(0);
  const [loading, setLoading] = useState(false);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [output, setOutput] = useState('');
  const [stderr, setStderr] = useState('');
  const [diagnostics, setDiagnostics] = useState('');
  const [runStatus, setRunStatus] = useState('Ready');
  const [elapsed, setElapsed] = useState<number | null>(null);
  const [exitCode, setExitCode] = useState<number | null>(null);
  const [timeout, setTimeout] = useState(5);
  const [executionId, setExecutionId] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [sourceVisible, setSourceVisible] = useState(true);
  const [inspectorVisible, setInspectorVisible] = useState(true);
  const [collapsed, setCollapsed] = useState(false);
  const [fitVersion, setFitVersion] = useState(0);
  const [resetVersion, setResetVersion] = useState(0);
  const [lightTheme, setLightTheme] = useState(() => localStorage.getItem('ast-theme') === 'light');
  const [consoleTab, setConsoleTab] = useState<'output' | 'errors' | 'diagnostics'>('output');
  const [searchText, setSearchText] = useState('');
  const [locateRequest, setLocateRequest] = useState<{ span: NonNullable<AstNodeDto['span']>; version: number } | null>(null);
  const astIndex = useMemo(() => ast ? indexAst(ast) : new Map(), [ast]);
  const selectedEntry = selectedId ? astIndex.get(selectedId) : undefined;
  const parentEntry = selectedEntry?.parentId ? astIndex.get(selectedEntry.parentId) : undefined;
  const selection: InspectorSelection | null = selectedId && selectedEntry ? { id: selectedId, ast: selectedEntry.ast, parent: parentEntry ? { id: selectedEntry.parentId!, ast: parentEntry.ast } : undefined } : null;

  async function handleAction() {
    if (mode === 'run') { await handleRun(); return; }
    if (loading) return;
    setLoading(true); setError(null); setAst(null); setSelectedId(null); setDiagnostics('');
    try {
      if (mode === 'education') {
        const parsed: ParseSuccess = await parseSource(source);
        setAst(parsed.ast); setNodeCount(parsed.nodeCount); setTokenCount(parsed.tokenCount);
      } else {
        const parsed = await parseRoslyn(source);
        setAst(parsed.ast); setNodeCount(parsed.nodeCount); setTokenCount(0);
        setDiagnostics(parsed.diagnostics.map(d => `${d.severity} ${d.id}${d.location ? ` (${d.location.line}:${d.location.column})` : ''}: ${d.message}`).join('\n'));
        if (!parsed.success) setError('Roslyn found syntax errors. The partial syntax tree is still available.');
      }
      setCollapsed(mode === 'roslyn'); setFitVersion(v => v + 1);
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'Parsing failed.'); }
    finally { setLoading(false); }
  }

  async function handleRun() {
    if (running) return;
    const id = crypto.randomUUID(); setExecutionId(id); setRunning(true); setError(null); setOutput(''); setStderr(''); setDiagnostics(''); setElapsed(null); setExitCode(null); setRunStatus('Compiling…');
    try {
      const result = await executeSource(source, id, timeout);
      setOutput(result.standardOutput); setStderr(result.standardError); setDiagnostics(result.diagnostics);
      setElapsed(result.elapsedMilliseconds); setExitCode(result.exitCode ?? null); setRunStatus(result.status);
      if (result.status !== 'completed') setError(result.status);
      setConsoleTab(result.diagnostics ? 'diagnostics' : result.standardError ? 'errors' : 'output');
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'Execution failed.'); setRunStatus('Failed'); }
    finally { setRunning(false); setExecutionId(null); }
  }
  async function handleStop() {
    if (!executionId) return;
    setRunStatus('Stopping…');
    try { await cancelExecution(executionId); } catch { /* The run request may already have completed. */ }
  }
  function onEditorKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') { event.preventDefault(); void handleAction(); }
  }
  function changeTheme() { setLightTheme(current => { const next = !current; localStorage.setItem('ast-theme', next ? 'light' : 'dark'); return next; }); }
  function loadExample() { setSource(examples[exampleIndex].code); setAst(null); setSelectedId(null); setError(null); }
  function updateSource(value: string) { setSource(value); setAst(null); setSelectedId(null); setError(null); }
  const workspaceClass = `workspace${sourceVisible ? '' : ' source-hidden'}${inspectorVisible ? '' : ' inspector-hidden'}`;
  const actionLabel = mode === 'run' ? 'Run C#' : mode === 'roslyn' ? 'Visualize AST' : 'Parse Toy C#';
  return <main className={`app-shell${lightTheme ? ' theme-light' : ''}`}>
    <header className="topbar">
      <div className="brand-lockup"><div className="brand-icon"><GitBranch size={18} /></div><div><strong>AST<span>WORKBENCH</span></strong><small>Local C# playground · .NET + Roslyn</small></div></div>
      <div className="topbar-center"><span className={`status-dot ${error ? 'is-error' : ast ? 'is-ready' : ''}`} />{running ? runStatus : error ? 'Needs attention' : ast ? 'Ready' : 'Local only'}</div>
      <div className="topbar-right"><span className="local-badge"><i />LOCAL</span><button className="icon-button theme-button" onClick={changeTheme} title="Toggle theme" aria-label="Toggle theme">{lightTheme ? <Moon size={16} /> : <Sun size={16} />}</button></div>
    </header>
    <section className="toolbar" aria-label="Playground toolbar"><div className="toolbar-left">
      <select className="mode-select" aria-label="Mode" value={mode} onChange={e => { setMode(e.target.value as Mode); setAst(null); setError(null); }}><option value="run">Run C# Program</option><option value="roslyn">Visualize Full C# AST</option><option value="education">Educational Parser</option></select>
      <button className="primary-button" onClick={() => void handleAction()} disabled={loading || running}>{loading ? <span className="button-spinner" /> : <Play size={14} fill="currentColor" />}{loading ? 'Working…' : actionLabel}</button>
      {running && <button className="tool-button stop-button" onClick={() => void handleStop()}><Square size={13} fill="currentColor" />Stop</button>}
      {mode === 'run' && <label className="timeout-control">Timeout <select aria-label="Execution timeout" value={timeout} onChange={e => setTimeout(Number(e.target.value))}><option value={1}>1s</option><option value={3}>3s</option><option value={5}>5s</option><option value={10}>10s</option><option value={15}>15s</option></select></label>}
      <div className="example-control"><select aria-label="Choose example" value={exampleIndex} onChange={e => setExampleIndex(Number(e.target.value))}>{examples.map((item, i) => <option key={item.name} value={i}>{item.name}</option>)}</select><ChevronDown size={13} /><button className="subtle-button" onClick={loadExample}><Braces size={14} />Load</button></div>
      <span className="toolbar-divider" /><button className="tool-button" disabled={!ast} onClick={() => { setSelectedId(null); setCollapsed(v => !v); }}>{collapsed ? <ChevronsDown size={15} /> : <ChevronsUp size={15} />}{collapsed ? 'Expand all' : 'Collapse all'}</button><button className="tool-button" disabled={!ast} onClick={() => setFitVersion(v => v + 1)} title="Fit graph"><Maximize size={14} />Fit</button><button className="tool-button" disabled={!ast} onClick={() => setResetVersion(v => v + 1)} title="Reset view"><RotateCcw size={14} /></button>
    </div><div className="toolbar-right"><button className={`icon-button ${!sourceVisible ? 'is-toggled' : ''}`} onClick={() => setSourceVisible(v => !v)} title="Toggle editor">{sourceVisible ? <PanelLeftClose size={16} /> : <PanelLeftOpen size={16} />}</button><button className={`icon-button ${!inspectorVisible ? 'is-toggled' : ''}`} onClick={() => setInspectorVisible(v => !v)} title="Toggle inspector">{inspectorVisible ? <PanelLeftClose size={16} /> : <PanelLeftOpen size={16} />}</button></div></section>
    <div className={workspaceClass}>
      {sourceVisible && <section className="panel source-panel"><div className="panel-heading"><div><span className="panel-index">01</span><h1>Editor</h1></div><span className="language-badge"><span />C#</span></div><div className="editor-caption"><Code2 size={13} /><span>Program.cs</span><span className="caption-spacer" /><span className="shortcut-hint"><kbd>Ctrl</kbd> <b>+</b> <kbd>Enter</kbd></span></div><CodeEditor value={source} onChange={updateSource} onKeyDown={onEditorKeyDown} locateRequest={locateRequest} />{error && <div className="error-panel"><TriangleAlert size={15} /><div><strong>{mode === 'run' ? 'Execution issue' : 'Parser message'}</strong><p>{error}</p></div></div>}<div className="source-footer"><span><i className="footer-dot" />{source.split('\n').length} lines</span><button className="clear-code" onClick={() => updateSource('')}>Clear</button><span>UTF-8</span></div></section>}
      <section className="panel graph-panel"><div className="panel-heading graph-heading"><div><span className="panel-index">02</span><h1>{mode === 'education' ? 'Educational AST' : 'C# Syntax Tree'}</h1></div><div className="graph-heading-right">{ast && <><input className="ast-search" aria-label="Search AST nodes" placeholder="Search nodes" value={searchText} onChange={event => setSearchText(event.target.value)} /><span className="metric-pill"><GitBranch size={12} />{nodeCount} nodes</span>{tokenCount > 0 && <span className="metric-pill"><Code2 size={12} />{tokenCount} tokens</span>}</>}<button className="icon-button compact-fit" disabled={!ast} onClick={() => setFitVersion(v => v + 1)} title="Fit graph"><Maximize size={14} /></button></div></div><div className="graph-content"><AstCanvas ast={ast} loading={loading} collapsed={collapsed} selectedId={selectedId} fitVersion={fitVersion} resetVersion={resetVersion} lightTheme={lightTheme} searchText={searchText} onSelect={setSelectedId} /></div><div className="graph-footer"><div className="legend"><Legend color="structure" label="Structure" /><Legend color="statement" label="Statement" /><Legend color="expression" label="Expression" /><Legend color="literal" label="Literal" /><Legend color="identifier" label="Identifier" /></div><div className="zoom-hint">Scroll to zoom <span>·</span> Drag to pan</div></div></section>
      {inspectorVisible && <section className="panel inspector-panel"><div className="panel-heading"><div><span className="panel-index">03</span><h1>Inspector</h1></div><span className="inspector-live">{selection ? 'SELECTED' : 'IDLE'}</span></div><Inspector selection={selection} onSelect={setSelectedId} onLocate={span => { setSourceVisible(true); setLocateRequest(current => ({ span, version: (current?.version ?? 0) + 1 })); }} /></section>}
    </div>
    <section className="console-panel"><div className="console-heading"><div><span className="panel-index">04</span><h1>Execution Console</h1><span className={`run-state ${runStatus.toLowerCase().replaceAll(' ', '-')}`}>{runStatus}</span></div><div className="console-meta">{elapsed !== null && <span>{elapsed} ms</span>}{exitCode !== null && <span>Exit {exitCode}</span>}</div></div><nav className="console-tabs"><button className={consoleTab === 'output' ? 'active' : ''} onClick={() => setConsoleTab('output')}>Output</button><button className={consoleTab === 'errors' ? 'active' : ''} onClick={() => setConsoleTab('errors')}>Standard error</button><button className={consoleTab === 'diagnostics' ? 'active' : ''} onClick={() => setConsoleTab('diagnostics')}>Build / parse diagnostics</button><button className="console-clear" onClick={() => { setOutput(''); setStderr(''); setDiagnostics(''); setElapsed(null); setExitCode(null); setRunStatus('Ready'); setError(null); }}>Clear</button></nav><pre className="console-output">{consoleTab === 'output' ? output || 'Run a program to see standard output here.' : consoleTab === 'errors' ? stderr || 'No standard error.' : diagnostics || 'No diagnostics.'}</pre></section>
    <footer className="app-footer"><div><span className={`footer-status ${error ? 'error' : ast ? 'success' : ''}`} />{error ? 'Attention required' : ast ? <><Check size={12} />{mode === 'run' ? runStatus : 'AST generated'}</> : 'Ready'}</div><span>{ast ? `${nodeCount} AST nodes${tokenCount ? ` · ${tokenCount} tokens` : ''}` : 'Editor ready'}</span><span className="footer-build">LOCAL PROCESS <b>·</b> .NET 8</span></footer>
  </main>;
}
function Legend({ color, label }: { color: string; label: string }) { return <span className="legend-item"><i className={`legend-color ${color}`} />{label}</span>; }

