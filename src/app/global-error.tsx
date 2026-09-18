'use client'

export default function GlobalError({ reset }: { error: Error; reset: () => void }) {
  return (
    <html lang="en">
      <body>
        <main>
          <h1>Something went wrong</h1>
          <p className="sub">The page failed to render. Try again, or check the server logs.</p>
          <button onClick={() => reset()}>Try again</button>
        </main>
      </body>
    </html>
  )
}
