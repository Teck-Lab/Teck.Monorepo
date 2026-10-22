import "./global.css";

export const metadata = {
  title: "Staff price lists | Teck",
  description: "Manage tenant product prices.",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
