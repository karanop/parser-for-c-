import { ChevronRight, Copy, MapPin, Network, Tag } from 'lucide-react';
import type { InspectorSelection, SourceSpan } from './types';

interface InspectorProps {
  selection: InspectorSelection | null;
  onSelect(id: string): void;
  onLocate?(span: SourceSpan): void;
}

export default function Inspector({ selection, onSelect, onLocate }: InspectorProps) {
  if (!selection) return (
    <div className="inspector-empty">
      <div className="inspector-empty-icon"><Network size={19} /></div>
      <strong>Nothing selected</strong>
      <p>Select a node in the graph to inspect its structure and properties.</p>
    </div>
  );

  const { ast, parent } = selection;
  const properties = Object.entries(ast.properties ?? {});
  return (
    <div className="inspector-content">
      <div className="inspector-type"><span className="type-dot" />{ast.type}</div>
      <h2 className="inspector-label">{ast.label}</h2>

      <section className="detail-section">
        <h3><Tag size={13} /> NODE DETAILS</h3>
        <Detail label="Node type" value={ast.type} />
        <Detail label="Label" value={ast.label} />
        {ast.value !== undefined && <Detail label="Value" value={String(ast.value)} mono />}
        {properties.map(([key, value]) => <Detail key={key} label={key} value={String(value)} mono />)}
      </section>

      {ast.span && onLocate && <button className="copy-value" onClick={() => onLocate(ast.span!)}>
        <MapPin size={13} /> Locate in editor
      </button>}

      <section className="detail-section">
        <h3><MapPin size={13} /> SOURCE</h3>
        {ast.location
          ? <Detail label="Location" value={`Line ${ast.location.line}, column ${ast.location.column}`} />
          : <p className="muted-note">The current parser AST does not track expression source locations.</p>}
        {ast.span && <><Detail label="Span" value={`${ast.span.start}–${ast.span.start + ast.span.length} (${ast.span.length} chars)`} mono /><Detail label="Range" value={`${ast.span.startLine}:${ast.span.startColumn} – ${ast.span.endLine}:${ast.span.endColumn}`} /><Detail label="Missing token" value={String(ast.span.isMissing)} /></>}
      </section>

      <section className="detail-section">
        <h3><Network size={13} /> RELATIONSHIPS</h3>
        {parent ? <div className="relationship-row"><span className="relationship-label">Parent</span><button onClick={() => onSelect(parent.id)}><span>{parent.ast.type}</span><b>{parent.ast.label}</b><ChevronRight size={13} /></button></div>
          : <Detail label="Parent" value="None (root node)" />}
        <div className="relationship-row">
          <span className="relationship-label">Children <small>{ast.children.length}</small></span>
          {ast.children.length ? ast.children.map((child, index) => {
            const childId = `${selection.id}.${index}`;
            return <button className="child-link" key={childId} onClick={() => onSelect(childId)}>
              <span>{child.type}</span><b>{child.label}</b><ChevronRight size={13} />
            </button>;
          }) : <span className="no-children">No child nodes</span>}
        </div>
      </section>

      {ast.value !== undefined && <button className="copy-value" onClick={() => navigator.clipboard?.writeText(String(ast.value))}>
        <Copy size={13} /> Copy value
      </button>}
    </div>
  );
}

function Detail({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return <div className="detail-row"><span>{label}</span><strong className={mono ? 'mono' : ''}>{value}</strong></div>;
}
