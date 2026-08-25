export interface ApiMonitor {
  id: string;
  endpointId: string;
  timeoutMs: number;
  expectedStatusCode: number;
  maxLatencyMs: number | null;
  consecutiveFailuresThreshold: number;
  intervalSeconds: number;
  enabled: boolean;
  ignoredPaths: string[];
}

export interface MonitorInput {
  timeoutMs: number;
  expectedStatusCode: number;
  maxLatencyMs: number | null;
  consecutiveFailuresThreshold: number;
  intervalSeconds: number;
  enabled: boolean;
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

export type ContractChangeClassification = 'Compatible' | 'Breaking';
export type ContractChangeType = 'Added' | 'Removed' | 'TypeChanged';
export type SchemaAnalysisStatus = 'Complete' | 'TooComplex';

export interface SchemaField {
  path: string;
  type: string;
}

export interface ContractFieldChange {
  path: string;
  changeType: ContractChangeType;
  oldType: string | null;
  newType: string | null;
}

export interface ContractChange {
  id: string;
  monitorId: string;
  detectedAt: string;
  fromSnapshotId: string;
  toSnapshotId: string;
  classification: ContractChangeClassification;
  changes: ContractFieldChange[];
}

export interface SchemaSnapshot {
  id: string;
  monitorId: string;
  capturedAt: string;
  structureHash: string;
  analysisStatus: SchemaAnalysisStatus;
  structure: SchemaField[];
}
