export interface ApiService {
  id: string;
  name: string;
  description: string | null;
  tags: string[];
  baseUrl: string;
}

export interface ApiServiceInput {
  name: string;
  description: string | null;
  tags: string[];
  baseUrl: string;
}

export type EndpointMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';

export interface ApiEndpoint {
  id: string;
  apiServiceId: string;
  path: string;
  method: EndpointMethod;
}

export interface EndpointInput {
  path: string;
  method: EndpointMethod;
}
