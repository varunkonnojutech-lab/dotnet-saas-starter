import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { apiGet } from '../api/client';
import type { PlanDto } from '../api/client';
import { useAuth } from '../auth/AuthContext';

const features = [
  {
    title: 'Multi-tenancy built in',
    body: 'Tenant resolution from the JWT, EF Core query filters for isolation, and quotas per plan. One tenant can never see another tenant\u2019s rows.'
  },
  {
    title: 'Auth that grows up',
    body: 'Email + password with JWT today; flip one config switch to accept Microsoft Entra ID tokens tomorrow. Roles: Owner, Admin, Member.'
  },
  {
    title: 'Stripe billing, test mode',
    body: 'Checkout, Billing Portal, and a webhook handler that keeps subscription state in sync. No real keys, no fake shortcuts.'
  },
  {
    title: 'Background jobs & email',
    body: 'A channel-based job queue with a hosted service, and an email abstraction with console, SMTP and SendGrid providers.'
  },
  {
    title: 'Runs anywhere',
    body: 'One command with Docker Compose. SQLite locally, SQL Server or Azure SQL in production. Terraform for Azure included.'
  },
  {
    title: 'Tests that prove it',
    body: 'Integration tests boot the real API and prove tenant isolation end to end — not just that the code compiles.'
  }
];

export default function Landing() {
  const { session } = useAuth();
  const [plans, setPlans] = useState<PlanDto[]>([]);

  useEffect(() => {
    apiGet<PlanDto[]>('/api/plans')
      .then(setPlans)
      .catch(() => setPlans([]));
  }, []);

  return (
    <div className="page">
      <header className="nav">
        <span className="logo">SaaS Starter</span>
        <nav>
          {session ? (
            <Link className="btn btn-primary" to="/dashboard">
              Open dashboard
            </Link>
          ) : (
            <>
              <Link className="btn btn-ghost" to="/login">
                Log in
              </Link>
              <Link className="btn btn-primary" to="/register">
                Start free
              </Link>
            </>
          )}
        </nav>
      </header>

      <section className="hero">
        <h1>Ship your SaaS this weekend, not next quarter.</h1>
        <p>
          A production-shaped starter for .NET 8 + React: multi-tenancy, JWT auth, Stripe billing,
          background jobs and email — wired up, tested, and ready to build on.
        </p>
        <div className="hero-actions">
          <Link className="btn btn-primary btn-lg" to="/register">
            Create your workspace
          </Link>
          <a className="btn btn-ghost btn-lg" href="#pricing">
            See pricing
          </a>
        </div>
        <p className="hint">
          Demo logins — <code>demo@acme.test</code> / <code>Demo123$</code> and{' '}
          <code>demo@globex.test</code> / <code>Demo123$</code> (two tenants, fully isolated).
        </p>
      </section>

      <section className="grid">
        {features.map((f) => (
          <article key={f.title} className="card">
            <h3>{f.title}</h3>
            <p>{f.body}</p>
          </article>
        ))}
      </section>

      <section id="pricing" className="pricing">
        <h2>Pricing</h2>
        <div className="grid">
          {plans.map((plan) => (
            <article key={plan.code} className="card price-card">
              <h3>{plan.name}</h3>
              <p className="price">
                ${(plan.monthlyPriceCents / 100).toFixed(0)}
                <span>/mo</span>
              </p>
              <p>{plan.description}</p>
              <ul>
                <li>{plan.maxProjects < 0 ? 'Unlimited' : plan.maxProjects} projects</li>
                <li>{plan.maxMembers < 0 ? 'Unlimited' : plan.maxMembers} members</li>
              </ul>
              <Link className="btn btn-primary" to="/register">
                Start with {plan.name}
              </Link>
            </article>
          ))}
        </div>
      </section>

      <footer className="footer">
        Built with the dotnet-saas-starter — .NET 8, React 18, EF Core, Stripe. MIT licensed.
      </footer>
    </div>
  );
}
