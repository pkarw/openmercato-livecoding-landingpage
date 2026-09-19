import type { Interest } from '@/lib/api'

export type ProductKey = 'openmercato' | 'aitechleaders'

export type Product = {
  key: ProductKey
  name: string
  site: string
  url: string
  tagline: string
  description: string
  listPrice: string
  priceNote: string
  highlights: string[]
}

export const PRODUCTS: Product[] = [
  {
    key: 'openmercato',
    name: 'Open Mercato Cloud',
    site: 'openmercatocloud.com',
    url: 'https://openmercatocloud.com/',
    tagline: 'Sandboxes run by agents',
    description:
      'Pre-configured sandboxes where coding agents build the business app your team keeps asking for — real data, migrations and a live preview in 30 seconds.',
    listPrice: '$75 / month',
    priceNote: 'Developer plan, first month',
    highlights: [
      'Agent-ready workspace (Claude Code, Codex, OpenCode)',
      'PostgreSQL, Redis, Meilisearch included',
      'Share a protected preview link instantly',
    ],
  },
  {
    key: 'aitechleaders',
    name: 'AI Tech Leaders',
    site: 'aitechleaders.pl',
    url: 'https://aitechleaders.pl/',
    tagline: 'AI-Assisted Engineering for your team',
    description:
      'A 5-week program — 25 lessons plus live sessions — that turns ad-hoc prompting into a shared engineering process your whole team can run.',
    listPrice: '4 990 PLN net',
    priceNote: '3 990 PLN net per person for teams of 5+',
    highlights: [
      'Specs, context engineering and agent workflows',
      'Review, security and QA safety nets',
      '14-day money-back guarantee',
    ],
  },
]

/** The picker is two toggles; the API stores one value. */
export function toInterest(selected: ProductKey[]): Interest | null {
  if (selected.length === 2) return 'both'
  if (selected.length === 1) return selected[0]
  return null
}

export function discounted(product: Product, percent: number): string {
  const match = product.listPrice.match(/([\d\s]+[\d])/)
  if (!match) return product.listPrice
  const amount = Number(match[1].replace(/\s/g, ''))
  if (!Number.isFinite(amount)) return product.listPrice
  const value = Math.round(amount * (1 - percent / 100))
  return product.listPrice.replace(match[1], value.toLocaleString('en-US').replace(/,/g, ' '))
}

export const PRIVACY_URL = 'https://openmercatocloud.com/privacy'

/**
 * Where this landing page is deployed — the target the hero QR code encodes.
 * Deployment-specific: the `app-<uuid>` host belongs to one deployment, so a
 * redeploy under a new id makes this dead and has to be updated here.
 */
export const APP_URL = 'https://app-54e0f829-998a-4972-a681-23fc364f32e8.apps-v2.openmercatocloud.com/'
