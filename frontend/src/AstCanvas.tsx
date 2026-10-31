import { useEffect, useMemo } from 'react';
import dagre from 'dagre';
import {
  Background, BackgroundVariant, ReactFlow, ReactFlowProvider,
  applyEdgeChanges, applyNodeChanges, useEdgesState, useNodesState,
  useReactFlow, type Edge, type EdgeChange, type NodeChange,
} from '@xyflow/react';
import AstFlowNode, { type AstFlowNodeType } from './AstFlowNode';
import type { AstNodeDto } from './types';

const nodeTypes = { ast: AstFlowNode };
const NODE_WIDTH = 210;
const NODE_HEIGHT = 92;

interface AstCanvasProps {
  ast: AstNodeDto | null;
  loading: boolean;
  collapsed: boolean;
  selectedId: string | null;
  fitVersion: number;
  resetVersion: number;
  lightTheme: boolean;
  searchText: string;
  onSelect(id: string): void;
}

export default function AstCanvas(props: AstCanvasProps) {
  return <ReactFlowProvider><Canvas {...props} /></ReactFlowProvider>;
}

function Canvas({ ast, loading, collapsed, selectedId, fitVersion, resetVersion, lightTheme, searchText, onSelect }: AstCanvasProps) {
  const [nodes, setNodes] = useNodesState<AstFlowNodeType>([]);
  const [edges, setEdges] = useEdgesState<Edge>([]);
  const { fitView, setViewport } = useReactFlow();

  const relationships = useMemo(() => ast ? indexAst(ast) : new Map<string, { ast: AstNodeDto; parentId?: string; parent?: AstNodeDto }>(), [ast]);

  useEffect(() => {
    if (!ast) { setNodes([]); setEdges([]); return; }
    const graph = makeGraph(ast, collapsed, selectedId, relationships, searchText);
    setNodes(graph.nodes);
    setEdges(graph.edges);
  }, [ast, collapsed, selectedId, relationships, searchText, setNodes, setEdges]);

  useEffect(() => {
    if (!ast || !fitVersion || nodes.length === 0) return;
    let secondFrame = 0;
    const frame = requestAnimationFrame(() => {
      secondFrame = requestAnimationFrame(() => { void fitView({ padding: 0.2, duration: 260 }); });
    });
    return () => { cancelAnimationFrame(frame); cancelAnimationFrame(secondFrame); };
  }, [ast, fitVersion, fitView, collapsed, nodes.length]);

  useEffect(() => {
    if (!resetVersion) return;
    void setViewport({ x: 0, y: 0, zoom: 1 }, { duration: 200 });
  }, [resetVersion, setViewport]);

  const onNodesChange = (changes: NodeChange<AstFlowNodeType>[]) => setNodes((current) => applyNodeChanges(changes, current));
  const onEdgesChange = (changes: EdgeChange[]) => setEdges((current) => applyEdgeChanges(changes, current));

  if (!ast) return (
    <div className="canvas-stage">
      <div className="canvas-empty">
      <div className="empty-graphic"><span className="empty-root" /><span className="empty-branch left" /><span className="empty-branch right" /><span className="empty-leaf one" /><span className="empty-leaf two" /><span className="empty-leaf three" /></div>
      <h2>Your syntax tree will appear here</h2>
      <p>Write a program in the editor, then parse it to explore its structure.</p>
      <span className="empty-shortcut">Tip <kbd>Ctrl</kbd> + <kbd>Enter</kbd> to parse</span>
      </div>
      {loading && <div className="canvas-loading"><span className="spinner" />Parsing source with the C# parser…</div>}
    </div>
  );

  return (
    <div className="canvas-stage">
      {loading && <div className="canvas-loading"><span className="spinner" />Parsing source with the C# parser…</div>}
      <ReactFlow<AstFlowNodeType, Edge>
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onNodeClick={(_, node) => onSelect(node.id)}
        nodesDraggable
        nodesConnectable={false}
        elementsSelectable
        minZoom={0.12}
        maxZoom={2.2}
        proOptions={{ hideAttribution: true }}
        fitView
      >
        <Background variant={BackgroundVariant.Dots} gap={22} size={1} color={lightTheme ? '#d9dfe8' : '#2a2d35'} />
      </ReactFlow>
    </div>
  );
}

function makeGraph(ast: AstNodeDto, collapsed: boolean, selectedId: string | null, index: Map<string, { ast: AstNodeDto; parentId?: string }>, searchText: string) {
  const flattened: { id: string; ast: AstNodeDto; parentId?: string }[] = [];
  const graphLimit = 1000;
  const visit = (node: AstNodeDto, id: string, parentId?: string) => {
    if (flattened.length >= graphLimit) return;
    flattened.push({ id, ast: node, parentId });
    if (!collapsed) node.children.forEach((child, childIndex) => { if (flattened.length < graphLimit) visit(child, `${id}.${childIndex}`, id); });
  };
  visit(ast, 'root');

  const connected = new Set<string>();
  if (selectedId) {
    connected.add(selectedId);
    const selection = index.get(selectedId);
    if (selection?.parentId) connected.add(selection.parentId);
    selection?.ast.children.forEach((_, childIndex) => connected.add(`${selectedId}.${childIndex}`));
  }

  const layout = new dagre.graphlib.Graph().setDefaultEdgeLabel(() => ({}));
  layout.setGraph({ rankdir: 'TB', ranksep: 48, nodesep: 24, marginx: 24, marginy: 24 });
  flattened.forEach(({ id }) => layout.setNode(id, { width: NODE_WIDTH, height: NODE_HEIGHT }));
  flattened.filter((node) => node.parentId).forEach(({ id, parentId }) => layout.setEdge(parentId!, id));
  dagre.layout(layout);

  const nodes: AstFlowNodeType[] = flattened.map(({ id, ast: node }) => {
    const position = layout.node(id);
    return {
      id,
      type: 'ast',
      position: { x: position.x - NODE_WIDTH / 2, y: position.y - NODE_HEIGHT / 2 },
      data: { ast: node, childCount: node.children.length, highlighted: connected.has(id) || (!!searchText && (node.type + ' ' + node.label).toLocaleLowerCase().includes(searchText.toLocaleLowerCase())) },
      selected: id === selectedId,
    };
  });

  const edges: Edge[] = flattened.filter((node) => node.parentId).map(({ id, parentId }) => {
    const active = !!selectedId && (selectedId === id || selectedId === parentId
      || index.get(selectedId)?.parentId === parentId || index.get(selectedId)?.parentId === id);
    return {
      id: `${parentId}->${id}`,
      source: parentId!,
      target: id,
      type: 'smoothstep',
      animated: false,
      style: { stroke: active ? '#8e9aff' : '#424650', strokeWidth: active ? 2.4 : 1.35 },
    };
  });
  return { nodes, edges };
}

function indexAst(root: AstNodeDto) {
  const index = new Map<string, { ast: AstNodeDto; parentId?: string; parent?: AstNodeDto }>();
  const visit = (ast: AstNodeDto, id: string, parentId?: string, parent?: AstNodeDto) => {
    index.set(id, { ast, parentId, parent });
    ast.children.forEach((child, childIndex) => visit(child, `${id}.${childIndex}`, id, ast));
  };
  visit(root, 'root');
  return index;
}

export { indexAst };
