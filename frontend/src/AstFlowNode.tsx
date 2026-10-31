import { Handle, Position, type Node, type NodeProps } from '@xyflow/react';
import type { AstGraphNodeData } from './types';

export type AstFlowNodeType = Node<AstGraphNodeData, 'ast'>;

export default function AstFlowNode({ data, selected }: NodeProps<AstFlowNodeType>) {
  return (
    <div className={`ast-node kind-${category(data.ast.type)}${selected ? ' selected' : ''}${data.highlighted ? ' related' : ''}`}>
      <Handle type="target" position={Position.Top} />
      <div className="ast-node-topline">
        <span className="ast-node-mark" />
        <span className="ast-node-type">{data.ast.type}</span>
      </div>
      <div className="ast-node-label" title={data.ast.label}>{data.ast.label}</div>
      <div className="ast-node-meta">{data.childCount === 1 ? '1 child' : `${data.childCount} children`}</div>
      <Handle type="source" position={Position.Bottom} />
    </div>
  );
}

export function category(type: string): string {
  if (type === 'Program' || type === 'Block') return 'structure';
  if (type === 'VariableDeclaration' || type === 'Assignment') return 'statement';
  if (type === 'BinaryExpression' || type === 'UnaryExpression') return 'expression';
  if (type === 'IntegerLiteral' || type === 'BooleanLiteral') return 'literal';
  if (type === 'Identifier') return 'identifier';
  if (type === 'IfStatement' || type === 'Condition' || type === 'Then' || type === 'Else') return 'conditional';
  if (type === 'PrintStatement') return 'output';
  return 'structure';
}
