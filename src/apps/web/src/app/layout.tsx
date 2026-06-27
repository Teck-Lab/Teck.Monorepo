import "./globals.css";

export const metadata = {
  title: "Teck Web",
  description: "Teck platform web app",
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
