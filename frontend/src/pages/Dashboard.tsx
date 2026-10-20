import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { apiDelete, apiGet, apiPatch, apiPost } from '../api/client';
import type { BillingStatus, PlanDto, ProjectDto, TenantDto, UserDto } from '../api/client';
import { useAuth } from '../auth/AuthContext';

type Tab = 'overview' | 'projects' | 'team' | 'billing';

export default function Dashboard({ initialTab = 'overview' }: { initialTab?: Tab }) {
  const { session, logout } = useAuth();
  const [tab, setTab] = useState<Tab>(initialTab);

  if (!session) return null;
  const { user, tenant } = session;

  return (
    <div className="page">
      <header className="nav">
        <span className="logo">{tenant.name}</span>
        <nav>
          <span className="hint">
            {user.fullName} · {user.role}
          </span>
          <Link className="btn btn-ghost" to="/">
            Landing
          </Link>
          <button className="btn btn-ghost" onClick={logout}>
            Log out
          </button>
        </nav>
      </header>

      <div className="tabs">
        {(['overview', 'projects', 'team', 'billing'] as Tab[]).map((t) => (
          <button
            key={t}
            className={t === tab ? 'tab active' : 'tab'}
            onClick={() => setTab(t)}
          >
            {t[0].toUpperCase() + t.slice(1)}
          </button>
        ))}
      </div>

      {tab === 'overview' && <Overview tenant={tenant} />}
      {tab === 'projects' && <ProjectsPanel />}
      {tab === 'team' && <TeamPanel />}
      {tab === 'billing' && <BillingPanel />}
    </div>
  );
}

function Overview({ tenant }: { tenant: TenantDto }) {
  const { isAdmin, refreshTenant } = useAuth();
  const [name, setName] = useState(tenant.name);
  const [message, setMessage] = useState<string | null>(null);

  async function rename(e: FormEvent) {
    e.preventDefault();
    await apiPatch('/api/tenants/current', { name });
    await refreshTenant();
    setMessage('Workspace renamed.');
  }

  return (
    <section className="grid">
      <article className="card">
        <h3>Workspace</h3>
        <p>
          <b>{tenant.name}</b> (<code>{tenant.slug}</code>)
        </p>
        <p>
          Plan: <b>{tenant.planName}</b> · Status: <b>{tenant.subscriptionStatus}</b>
        </p>
        {tenant.trialEndsAtUtc && (
          <p>Trial ends {new Date(tenant.trialEndsAtUtc).toLocaleDateString()}</p>
        )}
        {isAdmin && (
          <form onSubmit={rename} className="inline-form">
            <input value={name} onChange={(e) => setName(e.target.value)} aria-label="Workspace name" />
            <button className="btn btn-primary" type="submit">
              Rename
            </button>
          </form>
        )}
        {message && <p className="hint">{message}</p>}
      </article>
      <article className="card">
        <h3>How isolation works here</h3>
        <p>
          Every project you create belongs to this workspace only. The API scopes all data by the
          tenant id in your JWT — log in as the Globex demo user and you will see a completely
          different workspace.
        </p>
      </article>
    </section>
  );
}

function ProjectsPanel() {
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setProjects(await apiGet<ProjectDto[]>('/api/projects'));
  }, []);

  useEffect(() => {
    load().catch((e) => setError(e instanceof Error ? e.message : 'Failed to load'));
  }, [load]);

  async function create(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      await apiPost('/api/projects', { name, description: description || null });
      setName('');
      setDescription('');
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Create failed');
    }
  }

  async function remove(id: string) {
    await apiDelete(`/api/projects/${id}`);
    await load();
  }

  return (
    <section>
      <form className="card inline-form" onSubmit={create}>
        <input
          placeholder="Project name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          required
        />
        <input
          placeholder="Description (optional)"
          value={description}
          onChange={(e) => setDescription(e.target.value)}
        />
        <button className="btn btn-primary" type="submit">
          Add project
        </button>
      </form>
      {error && <p className="error">{error}</p>}
      <div className="grid">
        {projects.map((p) => (
          <article key={p.id} className="card">
            <h3>{p.name}</h3>
            <p>{p.description ?? 'No description.'}</p>
            <p className="hint">Created {new Date(p.createdAtUtc).toLocaleDateString()}</p>
            <button className="btn btn-ghost" onClick={() => remove(p.id)}>
              Delete
            </button>
          </article>
        ))}
        {projects.length === 0 && <p className="hint">No projects yet — create your first one.</p>}
      </div>
    </section>
  );
}

function TeamPanel() {
  const { isAdmin, session } = useAuth();
  const [members, setMembers] = useState<UserDto[]>([]);
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [role, setRole] = useState('Member');
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setMembers(await apiGet<UserDto[]>('/api/tenants/members'));
  }, []);

  useEffect(() => {
    load().catch((e) => setError(e instanceof Error ? e.message : 'Failed to load'));
  }, [load]);

  async function add(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      await apiPost('/api/tenants/members', { fullName, email, password, role });
      setFullName('');
      setEmail('');
      setPassword('');
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Add failed');
    }
  }

  async function remove(id: string) {
    await apiDelete(`/api/tenants/members/${id}`);
    await load();
  }

  return (
    <section>
      {isAdmin && (
        <form className="card inline-form" onSubmit={add}>
          <input
            placeholder="Full name"
            value={fullName}
            onChange={(e) => setFullName(e.target.value)}
            required
          />
          <input
            placeholder="Email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />
          <input
            placeholder="Temp password"
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
          <select value={role} onChange={(e) => setRole(e.target.value)} aria-label="Role">
            <option>Member</option>
            <option>Admin</option>
          </select>
          <button className="btn btn-primary" type="submit">
            Add member
          </button>
        </form>
      )}
      {error && <p className="error">{error}</p>}
      <div className="card">
        <table className="table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Email</th>
              <th>Role</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {members.map((m) => (
              <tr key={m.id}>
                <td>{m.fullName}</td>
                <td>{m.email}</td>
                <td>{m.role}</td>
                <td>
                  {isAdmin && m.id !== session?.user.id && m.role !== 'Owner' && (
                    <button className="btn btn-ghost" onClick={() => remove(m.id)}>
                      Remove
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function BillingPanel() {
  const { isAdmin, refreshTenant } = useAuth();
  const [status, setStatus] = useState<BillingStatus | null>(null);
  const [plans, setPlans] = useState<PlanDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    setStatus(await apiGet<BillingStatus>('/api/billing/status'));
    setPlans(await apiGet<PlanDto[]>('/api/plans'));
  }, []);

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    if (params.get('checkout') === 'success') {
      setNotice('Checkout completed — Stripe webhooks update your plan within a few seconds.');
    }
    load()
      .then(() => refreshTenant())
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load'));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [load]);

  async function checkout(planCode: string) {
    setError(null);
    try {
      const { url } = await apiPost<{ url: string }>('/api/billing/checkout', { planCode });
      window.location.href = url;
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Checkout failed');
    }
  }

  async function portal() {
    setError(null);
    try {
      const { url } = await apiPost<{ url: string }>('/api/billing/portal');
      window.location.href = url;
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Portal failed');
    }
  }

  if (!status) return <p className="hint">Loading billing…</p>;

  return (
    <section>
      {notice && <p className="notice">{notice}</p>}
      <article className="card">
        <h3>
          Current plan: {status.planName} · {status.subscriptionStatus}
        </h3>
        {status.trialEndsAtUtc && status.subscriptionStatus === 'Trialing' && (
          <p>Trial ends {new Date(status.trialEndsAtUtc).toLocaleDateString()}</p>
        )}
        {status.currentPeriodEndUtc && (
          <p>Current period ends {new Date(status.currentPeriodEndUtc).toLocaleDateString()}</p>
        )}
        {!status.stripeConfigured && (
          <p className="hint">
            Stripe is not configured on this server (test mode). Set <code>Stripe__SecretKey</code>{' '}
            and map prices under <code>Stripe__Prices</code> to enable checkout — see the README.
          </p>
        )}
        {isAdmin && status.stripeConfigured && (
          <button className="btn btn-ghost" onClick={portal}>
            Manage subscription (Stripe portal)
          </button>
        )}
      </article>
      <div className="grid">
        {plans
          .filter((p) => p.monthlyPriceCents > 0)
          .map((plan) => (
            <article key={plan.code} className="card price-card">
              <h3>{plan.name}</h3>
              <p className="price">
                ${(plan.monthlyPriceCents / 100).toFixed(0)}
                <span>/mo</span>
              </p>
              <p>{plan.description}</p>
              {status.planCode === plan.code ? (
                <p className="hint">Your current plan</p>
              ) : isAdmin ? (
                <button className="btn btn-primary" onClick={() => checkout(plan.code)}>
                  Upgrade to {plan.name}
                </button>
              ) : (
                <p className="hint">Ask an admin to upgrade.</p>
              )}
            </article>
          ))}
      </div>
      {error && <p className="error">{error}</p>}
    </section>
  );
}
