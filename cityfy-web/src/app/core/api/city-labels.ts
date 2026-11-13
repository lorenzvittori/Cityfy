import { City, GeneratedCityDetail } from './api.models';
import { CityMap } from './city-map.models';

/**
 * The API speaks English; names it generates itself (not MusicBrainz genre names) are
 * translated here, keyed by their stable id.
 */
const DISTRICT_LABELS: Record<string, string> = {
  other_genres: 'Altri generi',
};

export function localizeCity(city: City): City {
  return {
    ...city,
    districts: city.districts.map((d) => (DISTRICT_LABELS[d.districtId] ? { ...d, name: DISTRICT_LABELS[d.districtId] } : d)),
  };
}

export function localizeMap(map: CityMap): CityMap {
  return {
    ...map,
    districts: map.districts.map((d) => (DISTRICT_LABELS[d.districtId] ? { ...d, name: DISTRICT_LABELS[d.districtId] } : d)),
  };
}

export function localizeDetail(detail: GeneratedCityDetail): GeneratedCityDetail {
  return {
    ...detail,
    city: detail.city && localizeCity(detail.city),
    map: detail.map && localizeMap(detail.map),
  };
}
