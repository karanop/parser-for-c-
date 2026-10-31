import { useEffect, useRef, type KeyboardEvent } from 'react';
import type { SourceSpan } from './types';

interface CodeEditorProps {
  value: string;
  onChange(value: string): void;
  onKeyDown?(event: KeyboardEvent<HTMLTextAreaElement>): void;
  locateRequest?: { span: SourceSpan; version: number } | null;
}

const syntax = /(\/\/[^\n]*|"(?:\\.|[^"\\])*"|\b(?:int|bool|if|else|print|true|false)\b|\b\d+\b|&&|\|\||==|!=|<=|>=|[+\-*\/!<>=]|[{}();])/g;

export default function CodeEditor({ value, onChange, onKeyDown, locateRequest }: CodeEditorProps) {
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const highlightedRef = useRef<HTMLPreElement>(null);
  const gutterRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!locateRequest || !textareaRef.current) return;
    const textarea = textareaRef.current;
    const { start, length, startLine } = locateRequest.span;
    textarea.focus();
    textarea.setSelectionRange(start, start + length);
    textarea.scrollTop = Math.max(0, (startLine - 1) * 22 - 24);
  }, [locateRequest]);

  const syncScroll = () => {
    const textarea = textareaRef.current;
    if (!textarea) return;
    if (highlightedRef.current) {
      highlightedRef.current.scrollTop = textarea.scrollTop;
      highlightedRef.current.scrollLeft = textarea.scrollLeft;
    }
    if (gutterRef.current) gutterRef.current.scrollTop = textarea.scrollTop;
  };

  return (
    <div className="code-editor" aria-label="Source code editor">
      <div className="line-gutter" ref={gutterRef} aria-hidden="true">
        {Array.from({ length: Math.max(1, value.split('\n').length) }, (_, index) => <span key={index}>{index + 1}</span>)}
      </div>
      <div className="editor-stack">
        <pre className="highlight-layer" ref={highlightedRef} aria-hidden="true">{highlight(value)}{value.endsWith('\n') ? ' ' : ''}</pre>
        <textarea
          ref={textareaRef}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          onKeyDown={onKeyDown}
          onScroll={syncScroll}
          spellCheck={false}
          autoCapitalize="off"
          autoComplete="off"
          autoCorrect="off"
          aria-label="Program source"
        />
      </div>
    </div>
  );
}

function highlight(source: string) {
  const output = [];
  let previous = 0;
  for (const match of source.matchAll(syntax)) {
    const token = match[0];
    const index = match.index ?? 0;
    if (index > previous) output.push(source.slice(previous, index));
    const kind = token.startsWith('//') ? 'comment'
      : token.startsWith('"') ? 'string'
      : /^\d/.test(token) ? 'number'
      : /^(int|bool|if|else|print|true|false)$/.test(token) ? 'keyword'
      : ['+', '-', '*', '/', '!', '<', '>', '=', '==', '!=', '<=', '>=', '&&', '||'].includes(token) ? 'operator'
      : 'punctuation';
    output.push(<span className={`syntax-${kind}`} key={`${index}-${token}`}>{token}</span>);
    previous = index + token.length;
  }
  if (previous < source.length) output.push(source.slice(previous));
  return output;
}
