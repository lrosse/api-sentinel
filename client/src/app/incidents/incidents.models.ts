export type IncidentStatus = 'Open' | 'Recovered' | 'Resolved';

export type IncidentEventType =
  'Opened' | 'EvidenceAdded' | 'Recovered' | 'ResolvedManually' | 'CommentAdded';

export interface IncidentMonitor {
  id: string;
  endpointId: string;
  endpointMethod: string;
  endpointPath: string;
  apiServiceId: string;
  apiServiceName: string;
}

export interface IncidentListItem {
  id: string;
  monitorId: string;
  status: IncidentStatus;
  openedAt: string;
  recoveredAt: string | null;
  resolvedAt: string | null;
  triggerReason: string;
  rootCause: string | null;
  monitor: IncidentMonitor;
}

export interface IncidentEvent {
  id: string;
  occurredAt: string;
  eventType: IncidentEventType;
  description: string;
  relatedCheckRunId: string | null;
  relatedContractChangeId: string | null;
}

export interface IncidentDetail extends IncidentListItem {
  events: IncidentEvent[];
}

export interface ResolveIncidentInput {
  rootCause: string | null;
}
