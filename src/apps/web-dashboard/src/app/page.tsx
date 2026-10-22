import type { components } from "@teck/api-client";
import { auth, signIn, signOut } from "../auth";
import { savePrice } from "../lib/actions";
import { canManagePrices } from "../lib/authorization";
import { getPriceLists, getProducts } from "../lib/data";
import styles from "./page.module.css";

export default async function PriceListsPage() {
  const session = await auth();
  if (!session?.user) {
    return (
      <main className={styles.page}>
        <section className={styles.card}>
          <p className={styles.kicker}>TECK STAFF</p>
          <h1>Price list management</h1>
          <p>Sign in with your Teck account to view tenant price lists.</p>
          <form
            action={async () => {
              "use server";
              await signIn("keycloak");
            }}
          >
            <button className={styles.primary} type="submit">
              Sign in
            </button>
          </form>
        </section>
      </main>
    );
  }

  let products: components["schemas"]["CatalogApplicationProductsResponsesProductSummaryDto"][];
  let priceLists: components["schemas"]["PricingApplicationPricingResponsesPriceListDto"][];
  try {
    [products, priceLists] = await Promise.all([getProducts(), getPriceLists()]);
  } catch {
    return (
      <main className={styles.page}>
        <section className={styles.card}>
          <h1>Unable to load staff data</h1>
          <p>Your session may have expired, or your account may not have access to this tenant.</p>
          <form
            action={async () => {
              "use server";
              await signOut({ redirectTo: "/" });
            }}
          >
            <button className={styles.secondary} type="submit">
              Sign out
            </button>
          </form>
        </section>
      </main>
    );
  }
  const canManage = canManagePrices(session.user.roles);
  const productById = new Map(products.map((product) => [product.id, product]));

  return (
    <main className={styles.page}>
      <header className={styles.header}>
        <div>
          <p className={styles.kicker}>TECK STAFF</p>
          <h1>Price lists</h1>
        </div>
        <form
          action={async () => {
            "use server";
            await signOut({ redirectTo: "/" });
          }}
        >
          <button className={styles.secondary} type="submit">
            Sign out
          </button>
        </form>
      </header>
      <p className={styles.subtitle}>
        Signed in as {session.user.name ?? session.user.email}. Product and price data are scoped by
        the service to your organization.
      </p>
      {priceLists.length === 0 ? (
        <p>No price lists are available.</p>
      ) : (
        priceLists.map((priceList) => (
          <section className={styles.card} key={priceList.id}>
            <div className={styles.listHeading}>
              <div>
                <h2>{priceList.name}</h2>
                <p>
                  {priceList.currency} · {priceList.status}
                </p>
              </div>
            </div>
            <div className={styles.rows}>
              {products.map((product) => {
                const price = priceList.prices?.find((entry) => entry.productId === product.id);
                return (
                  <form action={savePrice} className={styles.row} key={product.id}>
                    <input type="hidden" name="listId" value={priceList.id} />
                    <input type="hidden" name="productId" value={product.id} />
                    <div>
                      <strong>{product.name}</strong>
                      <span>{productById.get(product.id)?.isActive ? "Active" : "Inactive"}</span>
                    </div>
                    <label>
                      <span>Amount ({priceList.currency})</span>
                      <input
                        aria-label={`${product.name} amount in ${priceList.currency}`}
                        name="amount"
                        type="number"
                        min="0"
                        max="999999999999"
                        step="any"
                        defaultValue={price?.amount ?? ""}
                        disabled={!canManage}
                        required
                      />
                    </label>
                    {canManage && (
                      <button className={styles.primary} type="submit">
                        Save price
                      </button>
                    )}
                  </form>
                );
              })}
            </div>
          </section>
        ))
      )}
      {!canManage && (
        <p className={styles.notice}>
          Read-only access: your account can view prices but cannot edit them.
        </p>
      )}
    </main>
  );
}
