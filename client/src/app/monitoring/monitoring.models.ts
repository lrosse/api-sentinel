export interface ApiMonitor {
  id: string;
  endpointId: string;
  timeoutMs: number;
  expectedStatusCode: number;
  maxLatencyMs: number | null;
  ignoredPaths: string[];
}

export interface MonitorInput {
  timeoutMs: number;
  expectedStatusCode: number;
  maxLatencyMs: number | null;
  ignoredPaths: string[];
}

export type CheckRunStatus = 'Success' | 'Failure';

export interface CheckRun {
  id: string;
  monitorId: string;
  startedAt: string;
  finishedAt: string;
  status: CheckRunStatus;
  httpStatusCode: number | null;
  latencyMs: number;
  errorMessage: string | null;
  responseBodySnippet: string | null;
}
