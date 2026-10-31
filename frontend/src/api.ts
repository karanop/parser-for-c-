import type { ExecutionResponse, ParseSuccess, RoslynParseResponse } from './types';

async function post<T>(path: string, body: unknown): Promise<T> {
  let response: Response;
  try { response = await fetch(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }); }
  catch { throw new Error('Could not reach the local parser API. Start the backend at http://127.0.0.1:5000.'); }
  const payload: unknown = await response.json().catch(() => null);
  if (!response.ok) throw new Error((payload as { error?: string } | null)?.error ?? `Request failed (${response.status}).`);
  return payload as T;
}
export const parseSource = (sourceCode: string) => post<ParseSuccess>('/api/parse', { sourceCode });
export const parseRoslyn = (sourceCode: string) => post<RoslynParseResponse>('/api/ast/roslyn', { sourceCode });
export const cancelExecution = (executionId: string) => post<{ cancelled: boolean }>('/api/execute/cancel', { executionId });
export const executeSource = (sourceCode: string, executionId: string, timeoutSeconds: number) =>
  post<ExecutionResponse>('/api/execute', { sourceCode, executionId, timeoutSeconds });
