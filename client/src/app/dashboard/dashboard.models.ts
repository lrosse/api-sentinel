import { CheckRunStatus } from '../monitoring/monitoring.models';

export interface DashboardCheckRun {
  status: CheckRunStatus;
  startedAt: string;
  latencyMs: number;
  httpStatusCode: number | null;
}

export interface DashboardMonitor {
  id: string;
  endpointId: string;
  endpointMethod: string;
  endpointPath: string;
  enabled: boolean;
  intervalSeconds: number;
  lastRun: DashboardCheckRun | null;
  consecutiveFailures: number;
}

export interface DashboardApiService {
  id: string;
  name: string;
  monitors: DashboardMonitor[];
}
