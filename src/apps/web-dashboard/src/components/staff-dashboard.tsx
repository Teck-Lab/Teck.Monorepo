"use client";

import type { paths } from "@teck/api-client";
import { signIn, signOut, useSession } from "next-auth/react";
import { type FormEvent, useState } from "react";

type CreateProduct = paths["/products"]["post"]["requestBody"]["content"]["application/json"];
type CreatedProduct = paths["/products"]["post"]["responses"]["201"]["content"]["application/json"];
type ProductRead =
  paths["/products/{productId}"]["get"]["responses"]["200"]["content"]["application/json"];
type ValidationProblem =
  paths["/products"]["post"]["responses"]["400"]["content"]["application/problem+json"];

export function StaffDashboard() {
  const { data: session, status } = useSession();
  const [values, setValues] = useState<CreateProduct>({
    name: "",
    sku: "",
    sellPriceCurrency: "USD",
  });
  const [result, setResult] = useState<ProductRead | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setProblem(null);
    setFieldErrors({});
    setResult(null);
    setBusy(true);
    try {
      const response = await fetch("/api/products", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(values),
      });
      const payload = await response.json();
      if (!response.ok) {
        const validation = payload as ValidationProblem;
        setFieldErrors(validation.errors ?? {});
        setProblem(
          validation.detail ?? "The product could not be created. Check your access and try again.",
        );
        return;
      }
      const created = payload as CreatedProduct;
      if (!created.id) throw new Error("The catalog did not return a product identifier.");
      const readResponse = await fetch(`/api/products/${encodeURIComponent(created.id)}`);
      if (!readResponse.ok)
        throw new Error(
          readResponse.status === 403
            ? "The product was created, but your account cannot read it back."
            : "The product was created, but the catalog could not confirm it.",
        );
      setResult((await readResponse.json()) as ProductRead);
    } catch (error) {
      setProblem(error instanceof Error ? error.message : "Could not reach the catalog gateway.");
    } finally {
      setBusy(false);
    }
  }

  if (status === "loading")
    return (
      <main className="grid min-h-screen place-items-center text-sm text-slate-500">
        Checking staff session…
      </main>
    );

  if (!session?.user) {
    return (
      <main className="grid min-h-screen place-items-center px-6 py-16">
        <section className="w-full max-w-md rounded-2xl border border-slate-200 bg-white p-8 shadow-sm">
          <span className="text-xs font-bold uppercase tracking-[0.2em] text-blue-700">
            TECK · OPERATIONS
          </span>
          <h1 className="mt-5 text-3xl font-semibold tracking-tight">Staff workspace</h1>
          <p className="mt-3 text-sm leading-6 text-slate-600">
            Sign in with your organization account to manage catalog products. Catalog access is
            authorized for your account by Keycloak.
          </p>
          {session?.error && (
            <p role="alert" className="mt-5 rounded-lg bg-amber-50 p-3 text-sm text-amber-900">
              Your staff session expired. Sign in again to continue.
            </p>
          )}
          <button
            type="button"
            onClick={() => signIn("keycloak")}
            className="mt-7 w-full rounded-lg bg-blue-700 px-4 py-3 text-sm font-semibold text-white transition hover:bg-blue-800 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2"
          >
            Sign in with Keycloak
          </button>
        </section>
      </main>
    );
  }

  return (
    <main className="min-h-screen px-5 py-8 sm:px-10 sm:py-12">
      <div className="mx-auto max-w-5xl">
        <header className="flex flex-wrap items-center justify-between gap-5 border-b border-slate-200 pb-7">
          <div>
            <span className="text-xs font-bold uppercase tracking-[0.2em] text-blue-700">
              TECK · OPERATIONS
            </span>
            <h1 className="mt-2 text-2xl font-semibold tracking-tight">Product catalog</h1>
            <p className="mt-1 text-sm text-slate-500">Create products for your organization.</p>
          </div>
          <div className="flex items-center gap-3">
            <span className="text-sm text-slate-600">
              {session.user?.name ?? session.user?.email ?? "Staff account"}
            </span>
            <button
              type="button"
              onClick={() => signOut({ callbackUrl: "/" })}
              className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium hover:bg-slate-50"
            >
              Sign out
            </button>
          </div>
        </header>
        <div className="grid gap-7 pt-8 lg:grid-cols-[1fr_0.9fr]">
          <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8">
            <h2 className="text-lg font-semibold">Create a product</h2>
            <p className="mt-1 text-sm text-slate-500">
              Product and default variant details. All fields validated by Catalog.
            </p>
            <form onSubmit={submit} className="mt-6 space-y-5">
              <label className="block text-sm font-medium">
                Product name <span className="text-red-600">*</span>
                <input
                  required
                  maxLength={200}
                  className="mt-2 w-full rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none transition focus:border-blue-600 focus:ring-2 focus:ring-blue-100"
                  value={values.name}
                  onChange={(event: React.ChangeEvent<HTMLInputElement>) =>
                    setValues({ ...values, name: event.target.value })
                  }
                  autoComplete="off"
                />
                {fieldErrors.Name && (
                  <span className="mt-1 block text-xs text-red-700">
                    {fieldErrors.Name.join(" ")}
                  </span>
                )}
              </label>
              <label className="block text-sm font-medium">
                SKU <span className="text-red-600">*</span>
                <input
                  required
                  maxLength={64}
                  className="mt-2 w-full rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none transition focus:border-blue-600 focus:ring-2 focus:ring-blue-100"
                  value={values.sku}
                  onChange={(event: React.ChangeEvent<HTMLInputElement>) =>
                    setValues({ ...values, sku: event.target.value })
                  }
                  autoComplete="off"
                />
                {fieldErrors.Sku && (
                  <span className="mt-1 block text-xs text-red-700">
                    {fieldErrors.Sku.join(" ")}
                  </span>
                )}
              </label>
              <label className="block text-sm font-medium">
                Description <span className="font-normal text-slate-400">Optional</span>
                <textarea
                  maxLength={4000}
                  rows={3}
                  className="mt-2 w-full rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none transition focus:border-blue-600 focus:ring-2 focus:ring-blue-100"
                  value={values.description ?? ""}
                  onChange={(event: React.ChangeEvent<HTMLTextAreaElement>) =>
                    setValues({ ...values, description: event.target.value || null })
                  }
                />
                {fieldErrors.Description && (
                  <span className="mt-1 block text-xs text-red-700">
                    {fieldErrors.Description.join(" ")}
                  </span>
                )}
              </label>
              <div className="grid grid-cols-[1fr_6rem] gap-4">
                <label className="block text-sm font-medium">
                  Sell price{" "}
                  <input
                    type="number"
                    min="0.01"
                    step="0.01"
                    className="mt-2 w-full rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none transition focus:border-blue-600 focus:ring-2 focus:ring-blue-100"
                    value={values.sellPriceAmount ?? ""}
                    onChange={(event: React.ChangeEvent<HTMLInputElement>) =>
                      setValues({
                        ...values,
                        sellPriceAmount: event.target.value
                          ? Number(event.target.value)
                          : undefined,
                      })
                    }
                  />
                  {fieldErrors.SellPriceAmount && (
                    <span className="mt-1 block text-xs text-red-700">
                      {fieldErrors.SellPriceAmount.join(" ")}
                    </span>
                  )}
                </label>
                <label className="block text-sm font-medium">
                  Currency{" "}
                  <input
                    required
                    minLength={3}
                    maxLength={3}
                    className="mt-2 w-full rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none transition focus:border-blue-600 focus:ring-2 focus:ring-blue-100"
                    value={values.sellPriceCurrency}
                    onChange={(event: React.ChangeEvent<HTMLInputElement>) =>
                      setValues({ ...values, sellPriceCurrency: event.target.value.toUpperCase() })
                    }
                  />
                  {fieldErrors.SellPriceCurrency && (
                    <span className="mt-1 block text-xs text-red-700">
                      {fieldErrors.SellPriceCurrency.join(" ")}
                    </span>
                  )}
                </label>
              </div>
              {problem && (
                <p
                  role="alert"
                  className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800"
                >
                  {problem}
                </p>
              )}
              <button
                type="submit"
                disabled={busy}
                className="w-full rounded-lg bg-blue-700 px-4 py-3 text-sm font-semibold text-white transition hover:bg-blue-800 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 disabled:cursor-wait disabled:opacity-60"
              >
                {busy ? "Creating and confirming…" : "Create product"}
              </button>
            </form>
          </section>
          <section
            aria-live="polite"
            className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm sm:p-8"
          >
            <h2 className="text-lg font-semibold">Catalog confirmation</h2>
            <p className="mt-1 text-sm text-slate-500">
              The product is confirmed only after a fresh authorized catalog read.
            </p>
            {result ? (
              <div className="mt-6 rounded-xl border border-emerald-200 bg-emerald-50 p-5">
                <p className="text-sm font-semibold text-emerald-900">
                  Product created and verified
                </p>
                <h3 className="mt-3 text-lg font-semibold text-slate-900">{result.name}</h3>
                <p className="mt-1 text-sm text-slate-600">
                  SKU {result.variants?.[0]?.sku ?? "—"}
                </p>
                <p className="mt-2 text-xs text-slate-500">ID {result.id}</p>
                {result.variants?.[0]?.sellPriceAmount != null && (
                  <p className="mt-3 text-sm font-medium text-slate-800">
                    {result.variants[0].sellPriceCurrency}{" "}
                    {result.variants[0].sellPriceAmount.toFixed(2)}
                  </p>
                )}
              </div>
            ) : (
              <div className="mt-6 grid min-h-44 place-items-center rounded-xl border border-dashed border-slate-300 p-6 text-center text-sm text-slate-500">
                A successful create followed by a fresh product read will appear here.
              </div>
            )}
          </section>
        </div>
      </div>
    </main>
  );
}
