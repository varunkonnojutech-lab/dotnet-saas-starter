// Typed API client for the SaaS Starter backend. All calls are same-origin:
// Vite (dev) and nginx (Docker) both proxy /api to the API container.

export interface UserDto {
  id: string;
  fullName: string;
  email: string;
  role: string;
}

export interface TenantDto {
  id: string;
  name: string;
  slug: string;
  planCode: string;
  planName: string;
  subscriptionStatus: string;
  trialEndsAtUtc: string | null;
  currentPeriodEndUtc: string | null;
}

export interface AuthResponse {
  token: string;
  expiresAtUtc: string;
  user: UserDto;
  tenant: TenantDto;
}

export interface PlanDto {
  code: string;
  name: string;
  description: string;
  monthlyPriceCents: number;
  maxProjects: number;
  maxMembers: number;
}

export interface ProjectDto {
  id: string;
  name: string;
  description: string | null;
  createdByUserId: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface BillingStatus {
  planCode: string;
  planName: string;
  subscriptionStatus: string;
  trialEndsAtUtc: string | null;
  currentPeriodEndUtc: string | null;
  stripeConfigured: boolean;
}

const SESSION_KEY = 'saas-starter-session';

export function loadSession(): AuthResponse | null {
  const raw = localStorage.getItem(SESSION_KEY);
  return raw ? (JSON.parse(raw) as AuthResponse) : null;
}

export function saveSession(session: AuthResponse | null) {
  if (session) {
    localStorage.setItem(SESSION_KEY, JSON.stringify(session));
  } else {
    localStorage.removeItem(SESSION_KEY);
  }
}

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const session = loadSession();
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(init.headers as Record<string, string> | undefined)
  };
  if (session?.token) {
    headers['Authorization'] = `Bearer ${session.token}`;
  }

  const response = await fetch(path, { ...init, headers });
  if (!response.ok) {
    let message = `Request failed (${response.status})`;
    try {
      const problem = (await response.json()) as { detail?: string; title?: string };
      message = problem.detail ?? problem.title ?? message;
    } catch {
      // Non-JSON error body — keep the generic message.
    }
    throw new ApiError(response.status, message);
  }

  if (response.status === 204) {
    return undefined as T;
  }
  return (await response.json()) as T;
}

export const apiGet = <T>(path: string) => api<T>(path);
export const apiPost = <T>(path: string, body?: unknown) =>
  api<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) });
export const apiPatch = <T>(path: string, body: unknown) =>
  api<T>(path, { method: 'PATCH', body: JSON.stringify(body) });
export const apiDelete = (path: string) => api<void>(path, { method: 'DELETE' });
