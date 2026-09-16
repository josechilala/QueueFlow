import { createAccessOrigins } from './access-links';

export function getConfiguredAccessOrigins() {
  return createAccessOrigins({
    QUEUEFLOW_PUBLIC_URL: process.env.QUEUEFLOW_PUBLIC_URL,
    QUEUEFLOW_CUSTOMER_URL: process.env.QUEUEFLOW_CUSTOMER_URL,
    NEXT_PUBLIC_QUEUEFLOW_ATTENDANT_URL: process.env.NEXT_PUBLIC_QUEUEFLOW_ATTENDANT_URL,
    NEXT_PUBLIC_QUEUEFLOW_DISPLAY_URL: process.env.NEXT_PUBLIC_QUEUEFLOW_DISPLAY_URL,
  }, process.env.NODE_ENV === 'production');
}
