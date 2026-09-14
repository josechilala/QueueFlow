import { AvailabilityView } from '../../../../components/availability-view';
export default function AvailabilityPage(props: { params: Promise<{ branchPublicId: string; servicePublicId: string }>; searchParams: Promise<{ date?: string; reschedule?: string }> }) {
  return <AvailabilityView {...props} />;
}
