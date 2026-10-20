import { createContext, useCallback, useContext, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { apiGet, apiPost, loadSession, saveSession } from '../api/client';
import type { AuthResponse } from '../api/client';

interface AuthContextValue {
  session: AuthResponse | null;
  isAdmin: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (workspaceName: string, fullName: string, email: string, password: string) => Promise<void>;
  logout: () => void;
  refreshTenant: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<AuthResponse | null>(() => loadSession());

  const apply = useCallback((next: AuthResponse | null) => {
    saveSession(next);
    setSession(next);
  }, []);

  const login = useCallback(
    async (email: string, password: string) => {
      const result = await apiPost<AuthResponse>('/api/auth/login', { email, password });
      apply(result);
    },
    [apply]
  );

  const register = useCallback(
    async (workspaceName: string, fullName: string, email: string, password: string) => {
      const result = await apiPost<AuthResponse>('/api/auth/register', {
        workspaceName,
        fullName,
        email,
        password
      });
      apply(result);
    },
    [apply]
  );

  const logout = useCallback(() => apply(null), [apply]);

  const refreshTenant = useCallback(async () => {
    if (!session) return;
    const me = await apiGet<{ user: AuthResponse['user']; tenant: AuthResponse['tenant'] }>(
      '/api/auth/me'
    ).catch(() => null);
    if (me) {
      apply({ ...session, user: me.user, tenant: me.tenant });
    }
  }, [apply, session]);

  const value = useMemo<AuthContextValue>(
    () => ({
      session,
      isAdmin: session?.user.role === 'Owner' || session?.user.role === 'Admin',
      login,
      register,
      logout,
      refreshTenant
    }),
    [session, login, register, logout, refreshTenant]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error('useAuth must be used inside <AuthProvider>');
  }
  return ctx;
}
