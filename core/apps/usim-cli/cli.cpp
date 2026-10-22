// The usim command line: queries against a data pack, printed as JSON or CSV (SPEC FR-CLI-01, P4-07).

#include "cli.hpp"

#include "usim/sim/simulation.hpp"
#include "usim/version.hpp"

#include <algorithm>
#include <cctype>
#include <charconv>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <map>
#include <optional>
#include <string_view>
#include <system_error>
#include <vector>

namespace usim::cli {

namespace {

constexpr std::string_view k_usage = R"(usim - UniverseSim headless host

Usage:
  usim --version                      Print the core library version
  usim --help                         Show this help
  usim <command> [arguments] [options]

Commands:
  catalog                             Bodies, their sourced values and the sources
  coverage                            The time span the pack covers
  state BODY                          Position and velocity of BODY relative to --center
  orientation BODY                    Rotation from BODY's IAU body-fixed frame to --frame
  snapshot                            Every body in render coordinates, as a host receives them
  orbit BODY                          One revolution of BODY around --center (default: its usual center)

BODY and --center take a NAIF ID (399) or a catalog name ("Earth", "mars barycenter").

Options:
  --pack PATH       The data pack. Default: $USIM_PACK, else the newest data/packs/solar-system-<version>.uspack
                    in the current directory or one of its parents.
  --format F        json (default) or csv
  --time T          ISO 8601 with a scale, for example "2026-10-21T12:00:00 UTC" or "2000-01-01 TDB";
                    default J2000 (2000-01-01T12:00:00 TDB)
  --frame F         icrf, eclipj2000 (default), or body:BODY for a body-fixed frame (state, orientation)
  --center BODY     state: default the solar system barycenter; snapshot: the view center (default
                    the solar system barycenter); orbit: default the body's usual orbit center
  --km-per-unit K   snapshot: kilometers per render unit (default 1)
  --axes A          snapshot: core (default), unity, godot or unreal
  --enlarge F       snapshot: multiply radii by F (enlarged-bodies scale mode)

Times are TDB unless a scale says otherwise. Lengths are km, velocities km/s.
)";

// ---------------------------------------------------------------------------------------------------
// Output helpers.

std::string number(double value) {
    char buffer[64];
    const auto result = std::to_chars(buffer, buffer + sizeof buffer, value);
    return result.ec == std::errc() ? std::string(buffer, result.ptr) : std::string("null");
}

std::string json_string(std::string_view text) {
    std::string out = "\"";
    for (const char c : text) {
        switch (c) {
        case '"':
            out += "\\\"";
            break;
        case '\\':
            out += "\\\\";
            break;
        case '\n':
            out += "\\n";
            break;
        case '\t':
            out += "\\t";
            break;
        case '\r':
            out += "\\r";
            break;
        default:
            if (static_cast<unsigned char>(c) < 0x20) {
                constexpr char k_hex[] = "0123456789abcdef";
                out += "\\u00";
                out += k_hex[(c >> 4) & 0xf];
                out += k_hex[c & 0xf];
            } else {
                out += c;
            }
        }
    }
    return out + "\"";
}

std::string csv_field(std::string_view text) {
    if (text.find_first_of(",\"\n\r") == std::string_view::npos) {
        return std::string(text);
    }
    std::string out = "\"";
    for (const char c : text) {
        out += c == '"' ? std::string("\"\"") : std::string(1, c);
    }
    return out + "\"";
}

std::string json_vector(const math::Vec3& v) {
    return "[" + number(v.x) + "," + number(v.y) + "," + number(v.z) + "]";
}

std::string json_optional(const std::optional<double>& value) {
    return value ? number(*value) : std::string("null");
}

// ---------------------------------------------------------------------------------------------------
// Command line.

struct Options {
    std::string command;
    std::vector<std::string> positional;
    std::map<std::string, std::string, std::less<>> named;

    [[nodiscard]] std::optional<std::string> get(std::string_view name) const {
        const auto it = named.find(name);
        return it == named.end() ? std::nullopt : std::optional(it->second);
    }
};

class UsageError {
public:
    explicit UsageError(std::string message) : message_(std::move(message)) {}
    [[nodiscard]] const std::string& message() const noexcept { return message_; }

private:
    std::string message_;
};

constexpr std::string_view k_value_options[] = {"--pack",   "--format",      "--time", "--frame",
                                                "--center", "--km-per-unit", "--axes", "--enlarge"};

std::optional<Options> parse_args(std::span<const std::string> args, std::string& problem) {
    Options options;
    for (std::size_t i = 0; i < args.size(); ++i) {
        const std::string& arg = args[i];
        if (arg.starts_with("--")) {
            if (std::find(std::begin(k_value_options), std::end(k_value_options), arg) ==
                std::end(k_value_options)) {
                problem = "unknown option " + arg;
                return std::nullopt;
            }
            if (i + 1 >= args.size()) {
                problem = "option " + arg + " needs a value";
                return std::nullopt;
            }
            options.named[arg] = args[++i];
        } else if (options.command.empty()) {
            options.command = arg;
        } else {
            options.positional.push_back(arg);
        }
    }
    return options;
}

std::string lowercase(std::string_view text) {
    std::string out(text);
    std::transform(out.begin(), out.end(), out.begin(),
                   [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    return out;
}

// A body given as a NAIF ID or a catalog name (case-insensitive).
Result<catalog::BodyId> resolve_body(const sim::Simulation& sim, std::string_view text) {
    std::int64_t id = 0;
    const auto [end, ec] = std::from_chars(text.data(), text.data() + text.size(), id);
    if (ec == std::errc() && end == text.data() + text.size()) {
        return catalog::BodyId::naif(id);
    }
    const std::string wanted = lowercase(text);
    for (const catalog::Body& body : sim.catalog().bodies()) {
        if (lowercase(body.name) == wanted) {
            return body.id;
        }
    }
    return make_error(ErrorCode::not_found, "no body named \"" + std::string(text) + "\" in the catalog");
}

Result<frames::FrameId> resolve_frame(const sim::Simulation& sim, const Options& options) {
    const std::string text = lowercase(options.get("--frame").value_or("eclipj2000"));
    if (text == "icrf" || text == "j2000") {
        return frames::FrameId::icrf();
    }
    if (text == "eclipj2000") {
        return frames::FrameId::eclipj2000();
    }
    if (text.starts_with("body:")) {
        Result<catalog::BodyId> body = resolve_body(sim, std::string_view(*options.get("--frame")).substr(5));
        if (!body) {
            return body.error();
        }
        return frames::FrameId::body_fixed(body.value());
    }
    throw UsageError("unknown frame \"" + *options.get("--frame") + "\" (icrf, eclipj2000 or body:BODY)");
}

Result<time::Instant> resolve_time(const sim::Simulation& sim, const Options& options) {
    const auto text = options.get("--time");
    if (!text) {
        return time::Instant(); // J2000 TDB
    }
    return sim.pack().time_scales().parse(*text, time::Calendar::mixed);
}

std::optional<std::filesystem::path> find_pack(const Options& options) {
    if (const auto pack = options.get("--pack")) {
        return std::filesystem::path(*pack);
    }
    if (const char* env = std::getenv("USIM_PACK"); env != nullptr && *env != '\0') {
        return std::filesystem::path(env);
    }
    std::error_code ec;
    for (std::filesystem::path dir = std::filesystem::current_path(ec); !ec && !dir.empty();
         dir = dir.parent_path()) {
        std::vector<std::filesystem::path> found;
        for (const auto& entry : std::filesystem::directory_iterator(dir / "data" / "packs", ec)) {
            const std::string name = entry.path().filename().string();
            // solar-system-<version>.uspack: the default pack, not optional ones such as
            // solar-system-moons-<version>.uspack.
            constexpr std::string_view prefix = "solar-system-";
            if (name.starts_with(prefix) && name.size() > prefix.size() &&
                std::isdigit(static_cast<unsigned char>(name[prefix.size()])) != 0 &&
                name.ends_with(".uspack")) {
                found.push_back(entry.path());
            }
        }
        if (!found.empty()) {
            std::sort(found.begin(), found.end());
            return found.back();
        }
        ec.clear();
        if (dir == dir.parent_path()) {
            break;
        }
    }
    return std::nullopt;
}

// ---------------------------------------------------------------------------------------------------
// Formatting shared by the commands.

struct Context {
    const sim::Simulation& sim;
    const Options& options;
    bool csv = false;
    std::ostream& out;
};

std::string tdb_text(const Context& c, time::Instant t) {
    const auto text = c.sim.pack().time_scales().format(t, time::TimeScale::tdb, time::Calendar::mixed, 3);
    return text ? text.value() : std::string();
}

std::string body_name(const Context& c, catalog::BodyId id) {
    const catalog::Body* body = c.sim.catalog().find(id);
    return body != nullptr ? body->name : catalog::to_string(id);
}

std::string json_body(const Context& c, catalog::BodyId id) {
    return "{\"id\":" + json_string(catalog::to_string(id)) + ",\"name\":" + json_string(body_name(c, id)) +
           "}";
}

std::string json_time(const Context& c, time::Instant t) {
    return "{\"tdb\":" + json_string(tdb_text(c, t)) + ",\"tdb_seconds\":" + number(t.seconds_since_j2000()) +
           "}";
}

std::string json_quantity(const catalog::Quantity& q) {
    if (!q.has_value()) {
        return "null";
    }
    return "{\"value\":" + number(*q.value) + ",\"unit\":" + json_string(catalog::to_string(q.unit)) +
           ",\"uncertainty\":" + json_optional(q.uncertainty) +
           ",\"source\":" + json_string(q.source.source_id) +
           ",\"locator\":" + json_string(q.source.locator) + ",\"derivation\":" + json_string(q.derivation) +
           "}";
}

std::string snapshot_flags(std::uint32_t flags) {
    std::string out;
    const auto add = [&](std::uint32_t bit, const char* name) {
        if ((flags & bit) != 0) {
            out += (out.empty() ? "" : "|") + std::string(name);
        }
    };
    add(sim::snapshot_approximate, "approximate");
    add(sim::snapshot_no_position, "no-position");
    add(sim::snapshot_no_orientation, "no-orientation");
    add(sim::snapshot_no_radius, "no-radius");
    return out;
}

// ---------------------------------------------------------------------------------------------------
// Commands. Each returns a Result so failures print one message.

Result<void> cmd_catalog(const Context& c) {
    const auto& chain = c.sim.ephemeris();
    const auto position = [&](catalog::BodyId id) {
        return !chain.contains(id) ? "none"
                                   : (chain.approximation(id) != nullptr ? "approximate" : "ephemeris");
    };
    if (c.csv) {
        c.out << "id,name,kind,primary,position,rotation_model,gm_km3_s2,mean_radius_km,radius_a_km,radius_b_"
                 "km,radius_c_km\n";
        for (const catalog::Body& b : c.sim.catalog().bodies()) {
            const auto value = [](const catalog::Quantity& q) {
                return q.has_value() ? number(*q.value) : std::string();
            };
            c.out << catalog::to_string(b.id) << ',' << csv_field(b.name) << ',' << catalog::to_string(b.kind)
                  << ',' << (b.primary ? catalog::to_string(*b.primary) : std::string()) << ','
                  << position(b.id) << ',' << (c.sim.frames().find_rotation(b.id) != nullptr ? "yes" : "no")
                  << ',' << value(b.gm) << ',' << value(b.mean_radius) << ',' << value(b.radius_a) << ','
                  << value(b.radius_b) << ',' << value(b.radius_c) << '\n';
        }
        return {};
    }
    const data::DataPack& pack = c.sim.pack();
    c.out << "{\"pack\":{\"name\":" << json_string(pack.metadata_value("pack.name").value_or(""))
          << ",\"version\":" << json_string(pack.metadata_value("pack.version").value_or(""))
          << ",\"format\":"
          << json_string(std::to_string(pack.header().version.major) + "." +
                         std::to_string(pack.header().version.minor))
          << "},\"coverage\":{\"start\":" << json_time(c, c.sim.coverage().start)
          << ",\"end\":" << json_time(c, c.sim.coverage().end) << "},\"bodies\":[";
    bool first = true;
    for (const catalog::Body& b : c.sim.catalog().bodies()) {
        const data::PositionApproximation* a = chain.approximation(b.id);
        c.out << (first ? "" : ",") << "{\"id\":" << json_string(catalog::to_string(b.id))
              << ",\"name\":" << json_string(b.name)
              << ",\"kind\":" << json_string(catalog::to_string(b.kind)) << ",\"primary\":"
              << (b.primary ? json_string(catalog::to_string(*b.primary)) : std::string("null"))
              << ",\"position\":" << json_string(position(b.id))
              << ",\"approximation\":" << (a != nullptr ? json_string(a->reason) : std::string("null"))
              << ",\"rotation_model\":" << (c.sim.frames().find_rotation(b.id) != nullptr ? "true" : "false")
              << ",\"gm\":" << json_quantity(b.gm) << ",\"mean_radius\":" << json_quantity(b.mean_radius)
              << ",\"radius_a\":" << json_quantity(b.radius_a)
              << ",\"radius_b\":" << json_quantity(b.radius_b)
              << ",\"radius_c\":" << json_quantity(b.radius_c) << "}";
        first = false;
    }
    c.out << "],\"sources\":[";
    first = true;
    for (const catalog::Source& s : c.sim.catalog().sources()) {
        c.out << (first ? "" : ",") << "{\"id\":" << json_string(s.id)
              << ",\"title\":" << json_string(s.title) << ",\"provider\":" << json_string(s.provider)
              << ",\"url\":" << json_string(s.url) << ",\"citation\":" << json_string(s.citation)
              << ",\"license\":" << json_string(s.license) << ",\"tier\":" << s.tier << "}";
        first = false;
    }
    c.out << "]}\n";
    return {};
}

Result<void> cmd_coverage(const Context& c) {
    const ephemeris::TimeInterval span = c.sim.coverage();
    if (c.csv) {
        c.out << "start_tdb,end_tdb,start_tdb_seconds,end_tdb_seconds\n"
              << tdb_text(c, span.start) << ',' << tdb_text(c, span.end) << ','
              << number(span.start.seconds_since_j2000()) << ',' << number(span.end.seconds_since_j2000())
              << '\n';
    } else {
        c.out << "{\"start\":" << json_time(c, span.start) << ",\"end\":" << json_time(c, span.end) << "}\n";
    }
    return {};
}

Result<catalog::BodyId> one_body(const Context& c) {
    if (c.options.positional.size() != 1) {
        throw UsageError("the " + c.options.command + " command takes one BODY");
    }
    return resolve_body(c.sim, c.options.positional[0]);
}

Result<void> cmd_state(const Context& c) {
    const Result<catalog::BodyId> body = one_body(c);
    const Result<catalog::BodyId> center = resolve_body(c.sim, c.options.get("--center").value_or("0"));
    const Result<frames::FrameId> frame = resolve_frame(c.sim, c.options);
    const Result<time::Instant> t = resolve_time(c.sim, c.options);
    if (!body || !center || !frame || !t) {
        return !body ? body.error() : (!center ? center.error() : (!frame ? frame.error() : t.error()));
    }
    const Result<sim::BodyState> s = c.sim.state(body.value(), center.value(), frame.value(), t.value());
    if (!s) {
        return s.error();
    }
    const auto& p = s.value().state.position_km;
    const auto& v = s.value().state.velocity_km_s;
    const bool approximate = s.value().quality.approximate;
    if (c.csv) {
        c.out
            << "body,center,frame,tdb,tdb_seconds,x_km,y_km,z_km,vx_km_s,vy_km_s,vz_km_s,model,approximate\n"
            << catalog::to_string(body.value()) << ',' << catalog::to_string(center.value()) << ','
            << frames::to_string(frame.value()) << ',' << tdb_text(c, t.value()) << ','
            << number(t.value().seconds_since_j2000()) << ',' << number(p.x) << ',' << number(p.y) << ','
            << number(p.z) << ',' << number(v.x) << ',' << number(v.y) << ',' << number(v.z) << ",ephemeris,"
            << (approximate ? "yes" : "no") << '\n';
    } else {
        c.out << "{\"body\":" << json_body(c, body.value()) << ",\"center\":" << json_body(c, center.value())
              << ",\"frame\":" << json_string(frames::to_string(frame.value()))
              << ",\"time\":" << json_time(c, t.value()) << ",\"position_km\":" << json_vector(p)
              << ",\"velocity_km_s\":" << json_vector(v)
              << ",\"model\":\"ephemeris\",\"approximate\":" << (approximate ? "true" : "false") << "}\n";
    }
    return {};
}

Result<void> cmd_orientation(const Context& c) {
    const Result<catalog::BodyId> body = one_body(c);
    const Result<frames::FrameId> frame = resolve_frame(c.sim, c.options);
    const Result<time::Instant> t = resolve_time(c.sim, c.options);
    if (!body || !frame || !t) {
        return !body ? body.error() : (!frame ? frame.error() : t.error());
    }
    const Result<frames::FrameTransform> transform =
        c.sim.frames().transform(frames::FrameId::body_fixed(body.value()), frame.value(), t.value());
    if (!transform) {
        return transform.error();
    }
    const math::Quat q = math::Quat::from_matrix(transform.value().rotation());
    const math::Mat3& m = transform.value().rotation();
    if (c.csv) {
        c.out << "body,frame,tdb,tdb_seconds,qw,qx,qy,qz,m00,m01,m02,m10,m11,m12,m20,m21,m22\n"
              << catalog::to_string(body.value()) << ',' << frames::to_string(frame.value()) << ','
              << tdb_text(c, t.value()) << ',' << number(t.value().seconds_since_j2000()) << ','
              << number(q.w) << ',' << number(q.x) << ',' << number(q.y) << ',' << number(q.z);
        for (const double x : m.m) {
            c.out << ',' << number(x);
        }
        c.out << '\n';
    } else {
        c.out << "{\"body\":" << json_body(c, body.value())
              << ",\"frame\":" << json_string(frames::to_string(frame.value()))
              << ",\"time\":" << json_time(c, t.value()) << ",\"quaternion_wxyz\":[" << number(q.w) << ','
              << number(q.x) << ',' << number(q.y) << ',' << number(q.z) << "],\"matrix_rows\":[";
        for (std::size_t r = 0; r < 3; ++r) {
            c.out << (r == 0 ? "" : ",") << json_vector(m.row(r));
        }
        c.out << "]}\n";
    }
    return {};
}

Result<presentation::AxisConvention> resolve_axes(const Options& options) {
    const std::string text = lowercase(options.get("--axes").value_or("core"));
    for (const auto axes : {presentation::AxisConvention::core, presentation::AxisConvention::unity,
                            presentation::AxisConvention::godot, presentation::AxisConvention::unreal}) {
        if (text == presentation::to_string(axes)) {
            return axes;
        }
    }
    throw UsageError("unknown axes \"" + text + "\" (core, unity, godot or unreal)");
}

double parse_number(const Options& options, std::string_view name, double fallback) {
    const auto text = options.get(name);
    if (!text) {
        return fallback;
    }
    double value = 0.0;
    const auto [end, ec] = std::from_chars(text->data(), text->data() + text->size(), value);
    if (ec != std::errc() || end != text->data() + text->size()) {
        throw UsageError("option " + std::string(name) + " needs a number, not \"" + *text + "\"");
    }
    return value;
}

Result<void> cmd_snapshot(const Context& c, sim::Simulation& sim) {
    if (!c.options.positional.empty()) {
        throw UsageError("the snapshot command takes no BODY; use --center");
    }
    const Result<catalog::BodyId> center = resolve_body(sim, c.options.get("--center").value_or("0"));
    const Result<frames::FrameId> frame = resolve_frame(sim, c.options);
    const Result<time::Instant> t = resolve_time(sim, c.options);
    const Result<presentation::AxisConvention> axes = resolve_axes(c.options);
    if (!center || !frame || !t || !axes) {
        return !center ? center.error() : (!frame ? frame.error() : (!t ? t.error() : axes.error()));
    }
    if (Result<sim::ClockLimit> set = sim.clock().set_time(t.value()); !set) {
        return set.error();
    }
    sim::SnapshotRequest request;
    request.center = center.value();
    request.frame = frame.value();
    request.view.km_per_render_unit = parse_number(c.options, "--km-per-unit", 1.0);
    request.view.axes = axes.value();
    if (c.options.get("--enlarge")) {
        request.view.scale_mode = presentation::ScaleMode::enlarged_bodies;
        request.view.body_scale = parse_number(c.options, "--enlarge", 1.0);
    }
    std::vector<sim::BodySnapshot> bodies(sim.snapshot_size());
    const Result<std::size_t> count = sim.fill_snapshot(request, bodies);
    if (!count) {
        return count.error();
    }
    const time::Instant now = sim.clock().now();
    if (c.csv) {
        c.out << "id,name,x,y,z,qw,qx,qy,qz,radius,flags\n";
        for (const sim::BodySnapshot& b : bodies) {
            c.out << catalog::to_string(b.body) << ',' << csv_field(body_name(c, b.body)) << ','
                  << number(b.render_position.x) << ',' << number(b.render_position.y) << ','
                  << number(b.render_position.z) << ',' << number(b.orientation.w) << ','
                  << number(b.orientation.x) << ',' << number(b.orientation.y) << ','
                  << number(b.orientation.z) << ',' << number(b.render_radius) << ','
                  << snapshot_flags(b.flags) << '\n';
        }
        return {};
    }
    c.out << "{\"time\":" << json_time(c, now) << ",\"clock_limit\":" << static_cast<int>(sim.clock().limit())
          << ",\"center\":" << json_body(c, center.value())
          << ",\"frame\":" << json_string(frames::to_string(frame.value()))
          << ",\"view\":{\"km_per_render_unit\":" << number(request.view.km_per_render_unit)
          << ",\"axes\":" << json_string(presentation::to_string(request.view.axes))
          << ",\"scale_mode\":" << json_string(presentation::to_string(request.view.scale_mode))
          << ",\"body_scale\":" << number(request.view.body_scale) << "},\"bodies\":[";
    for (std::size_t i = 0; i < bodies.size(); ++i) {
        const sim::BodySnapshot& b = bodies[i];
        c.out << (i == 0 ? "" : ",") << "{\"body\":" << json_body(c, b.body)
              << ",\"render_position\":" << json_vector(b.render_position) << ",\"orientation_wxyz\":["
              << number(b.orientation.w) << ',' << number(b.orientation.x) << ',' << number(b.orientation.y)
              << ',' << number(b.orientation.z) << "],\"render_radius\":" << number(b.render_radius)
              << ",\"flags\":" << json_string(snapshot_flags(b.flags)) << "}";
    }
    c.out << "]}\n";
    return {};
}

Result<void> cmd_orbit(const Context& c) {
    const Result<catalog::BodyId> body = one_body(c);
    if (!body) {
        return body.error();
    }
    const Result<catalog::BodyId> center = c.options.get("--center")
                                               ? resolve_body(c.sim, *c.options.get("--center"))
                                               : c.sim.default_orbit_center(body.value());
    const Result<frames::FrameId> frame = resolve_frame(c.sim, c.options);
    const Result<time::Instant> t = resolve_time(c.sim, c.options);
    if (!center || !frame || !t) {
        return !center ? center.error() : (!frame ? frame.error() : t.error());
    }
    const Result<sim::OrbitPath> path =
        c.sim.orbit_path(body.value(), center.value(), frame.value(), t.value());
    if (!path) {
        return path.error();
    }
    const sim::OrbitPath& p = path.value();
    if (c.csv) {
        c.out << "tdb_seconds,x_km,y_km,z_km\n";
        for (const sim::OrbitPathPoint& point : p.points) {
            c.out << number(point.t.seconds_since_j2000()) << ',' << number(point.position_km.x) << ','
                  << number(point.position_km.y) << ',' << number(point.position_km.z) << '\n';
        }
        return {};
    }
    c.out << "{\"body\":" << json_body(c, p.body) << ",\"center\":" << json_body(c, p.center)
          << ",\"frame\":" << json_string(frames::to_string(p.frame))
          << ",\"start\":" << json_time(c, p.start) << ",\"end\":" << json_time(c, p.end)
          << ",\"full_revolution\":" << (p.full_revolution ? "true" : "false")
          << ",\"approximate\":" << (p.approximate ? "true" : "false")
          << ",\"point_count\":" << p.points.size() << ",\"points_tdb_seconds_x_y_z_km\":[";
    for (std::size_t i = 0; i < p.points.size(); ++i) {
        const sim::OrbitPathPoint& point = p.points[i];
        c.out << (i == 0 ? "" : ",") << "[" << number(point.t.seconds_since_j2000()) << ','
              << number(point.position_km.x) << ',' << number(point.position_km.y) << ','
              << number(point.position_km.z) << "]";
    }
    c.out << "]}\n";
    return {};
}

} // namespace

int run(std::span<const std::string> args, std::ostream& out, std::ostream& err) {
    if (args.empty() || (args.size() == 1 && (args[0] == "--help" || args[0] == "-h"))) {
        out << k_usage;
        return k_exit_ok;
    }
    if (args.size() == 1 && args[0] == "--version") {
        out << "usim " << usim::version_string() << '\n';
        return k_exit_ok;
    }
    std::string problem;
    const std::optional<Options> options = parse_args(args, problem);
    constexpr std::string_view k_commands[] = {"catalog",     "coverage", "state",
                                               "orientation", "snapshot", "orbit"};
    if (!options ||
        std::find(std::begin(k_commands), std::end(k_commands), options->command) == std::end(k_commands)) {
        err << "usim: " << (options ? "unknown command \"" + options->command + "\"" : problem) << "\n\n"
            << k_usage;
        return k_exit_usage;
    }
    const std::string format = lowercase(options->get("--format").value_or("json"));
    if (format != "json" && format != "csv") {
        err << "usim: unknown format \"" << format << "\" (json or csv)\n";
        return k_exit_usage;
    }
    const std::optional<std::filesystem::path> pack = find_pack(*options);
    if (!pack) {
        err << "usim: no data pack found; pass --pack PATH or set USIM_PACK\n";
        return k_exit_usage;
    }
    Result<sim::Simulation> loaded = sim::Simulation::load_file(*pack);
    if (!loaded) {
        err << "usim: " << loaded.error().message << " (" << to_string(loaded.error().code) << ")\n";
        return k_exit_failed;
    }
    sim::Simulation& simulation = loaded.value();
    const Context context{simulation, *options, format == "csv", out};
    try {
        Result<void> result;
        const std::string& command = options->command;
        if (command == "catalog") {
            result = cmd_catalog(context);
        } else if (command == "coverage") {
            result = cmd_coverage(context);
        } else if (command == "state") {
            result = cmd_state(context);
        } else if (command == "orientation") {
            result = cmd_orientation(context);
        } else if (command == "snapshot") {
            result = cmd_snapshot(context, simulation);
        } else {
            result = cmd_orbit(context);
        }
        if (!result) {
            err << "usim: " << result.error().message << " (" << to_string(result.error().code) << ")\n";
            return k_exit_failed;
        }
    } catch (const UsageError& e) {
        err << "usim: " << e.message() << '\n';
        return k_exit_usage;
    }
    return k_exit_ok;
}

} // namespace usim::cli
