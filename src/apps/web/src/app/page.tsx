import { Button } from "@teck/ui";

export default function Home() {
  return (
    <main style={{ display: "grid", placeItems: "center", minHeight: "100dvh" }}>
      <div style={{ display: "flex", gap: 12 }}>
        <Button>Primary</Button>
        <Button variant="outline">Outline</Button>
      </div>
    </main>
  );
}
