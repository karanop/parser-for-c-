export interface SourceLocation { line: number; column: number; }
export interface SourceSpan { start: number; length: number; startLine: number; startColumn: number; endLine: number; endColumn: number; isMissing: boolean; }
export interface AstNodeDto {
  type: string; label: string; value?: string | number | boolean;
  properties: Record<string, string | number | boolean | null>; children: AstNodeDto[];
  location?: SourceLocation; span?: SourceSpan;
}
export interface TokenDto { kind: string; text: string; location: SourceLocation; offset: number; }
export interface ParseSuccess { success: true; ast: AstNodeDto; tokenCount: number; nodeCount: number; tokens: TokenDto[]; }
export interface ParseFailure { error: string; location?: SourceLocation; }
export interface RoslynParseResponse { success: boolean; ast: AstNodeDto; nodeCount: number; diagnostics: { id: string; severity: string; message: string; location?: SourceLocation }[]; }
export interface ExecutionResponse {
  executionId: string; compilationSucceeded: boolean; status: string; standardOutput: string; standardError: string;
  diagnostics: string; exitCode?: number; elapsedMilliseconds: number; timedOut: boolean; cancelled: boolean; outputTruncated: boolean;
}
export interface AstGraphNodeData extends Record<string, unknown> { ast: AstNodeDto; childCount: number; highlighted: boolean; }
export interface InspectorSelection { id: string; ast: AstNodeDto; parent?: { id: string; ast: AstNodeDto }; }
