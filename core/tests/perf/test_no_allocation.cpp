// P3-06, SPEC NFR-03: the per-frame ephemeris path allocates nothing. This executable replaces the global
// allocation functions with counting versions, so it is separate from the other tests.

#include "fixtures/test_pack_expected.hpp"
#include "usim/data/data_pack.hpp"
#include "usim/ephemeris/ephemeris_chain.hpp"
#include "usim/sim/simulation.hpp"
#include "validation/validation_support.hpp"

#include <catch2/catch_test_macros.hpp>

#include <array>
#include <atomic>
#include <cstddef>
#include <cstdlib>
#include <filesystem>
#include <new>

#ifdef _MSC_VER
#    include <malloc.h>
#endif

namespace {

std::atomic<std::size_t> g_allocations{0};

void* counted_allocation(std::size_t size) {
    g_allocations.fetch_add(1, std::memory_order_relaxed);
    if (void* p = std::malloc(size == 0 ? 1 : size)) {
        return p;
    }
    throw std::bad_alloc();
}

void* counted_aligned_allocation(std::size_t size, std::align_val_t alignment) {
    g_allocations.fetch_add(1, std::memory_order_relaxed);
    const auto align = static_cast<std::size_t>(alignment);
#ifdef _MSC_VER
    void* p = _aligned_malloc(size == 0 ? 1 : size, align);
#else
    // aligned_alloc needs a size that is a multiple of the alignment.
    void* p = std::aligned_alloc(align, ((size == 0 ? 1 : size) + align - 1) / align * align);
#endif
    if (p != nullptr) {
        return p;
    }
    throw std::bad_alloc();
}

void aligned_free(void* p) noexcept {
#ifdef _MSC_VER
    _aligned_free(p);
#else
    std::free(p);
#endif
}

} // namespace

// The replaceable global allocation functions ([new.delete]). The nothrow and array forms default to these.
void* operator new(std::size_t size) {
    return counted_allocation(size);
}
void* operator new[](std::size_t size) {
    return counted_allocation(size);
}
void* operator new(std::size_t size, std::align_val_t alignment) {
    return counted_aligned_allocation(size, alignment);
}
void* operator new[](std::size_t size, std::align_val_t alignment) {
    return counted_aligned_allocation(size, alignment);
}
void operator delete(void* p) noexcept {
    std::free(p);
}
void operator delete[](void* p) noexcept {
    std::free(p);
}
void operator delete(void* p, std::size_t) noexcept {
    std::free(p);
}
void operator delete[](void* p, std::size_t) noexcept {
    std::free(p);
}
void operator delete(void* p, std::align_val_t) noexcept {
    aligned_free(p);
}
void operator delete[](void* p, std::align_val_t) noexcept {
    aligned_free(p);
}
void operator delete(void* p, std::size_t, std::align_val_t) noexcept {
    aligned_free(p);
}
void operator delete[](void* p, std::size_t, std::align_val_t) noexcept {
    aligned_free(p);
}

using usim::catalog::BodyId;
using usim::ephemeris::EphemerisChain;
using usim::frames::FrameId;
using usim::time::Instant;

namespace {

// Evaluates every body relative to `center` at several instants, in both inertial frames, including
// failing queries (out of coverage, unknown body), and returns the number of allocations made meanwhile.
std::size_t allocations_during_snapshots(const EphemerisChain& chain, BodyId center, Instant first,
                                         double step_s) {
    std::array<usim::ephemeris::Evaluation, 64> out{};
    REQUIRE(chain.bodies().size() <= out.size());
    const std::size_t before = g_allocations.load();
    double checksum = 0.0;
    for (int frame = 0; frame < 40; ++frame) {
        const Instant t = first.plus_seconds(step_s * frame);
        const FrameId frame_id = frame % 2 == 0 ? FrameId::icrf() : FrameId::eclipj2000();
        std::size_t n = 0;
        for (const BodyId body : chain.bodies()) {
            out[n] = chain.evaluate(body, center, frame_id, t);
            checksum += out[n].state.position_km.x;
            ++n;
        }
        out[0] = chain.evaluate(BodyId::naif(123456789), center, frame_id, t); // unknown
        out[1] = chain.evaluate(chain.bodies().back(), center, frame_id,
                                t.plus_seconds(1.0e12)); // out of coverage
        out[2] = chain.evaluate(chain.bodies().back(), center, FrameId::body_fixed(center), t); // unsupported
    }
    const std::size_t after = g_allocations.load();
    CHECK(checksum == checksum); // keeps the work observable
    return after - before;
}

} // namespace

// Fills snapshots over many clock steps and returns the number of allocations made meanwhile.
std::size_t allocations_during_fill(usim::sim::Simulation& sim, BodyId center) {
    std::array<usim::sim::BodySnapshot, 64> out{};
    REQUIRE(sim.snapshot_size() <= out.size());
    usim::sim::SnapshotRequest request;
    request.center = center;
    request.view.km_per_render_unit = 1.0e6;
    request.view.axes = usim::presentation::AxisConvention::unity;
    REQUIRE(sim.clock().set_rate(86400.0).has_value());
    const std::size_t before = g_allocations.load();
    double checksum = 0.0;
    bool ok = true;
    for (int frame = 0; frame < 100; ++frame) {
        ok = sim.clock().advance(1.0 / 60.0).has_value() && ok;
        const auto count = sim.fill_snapshot(request, out);
        ok = count.has_value() && ok;
        checksum += out[0].render_position.x;
    }
    const std::size_t after = g_allocations.load();
    CHECK(ok);
    CHECK(checksum == checksum);
    return after - before;
}

TEST_CASE("the allocation counter sees allocations", "[allocation]") {
    // An explicit call, which the compiler may not elide the way it may elide a new-expression.
    const std::size_t before = g_allocations.load();
    void* volatile p = ::operator new(16);
    ::operator delete(p);
    CHECK(g_allocations.load() == before + 1);
}

TEST_CASE("ephemeris evaluation allocates nothing (synthetic pack)", "[allocation]") {
    namespace expected = usim::test::pack;
    const auto pack = usim::data::DataPack::load_file(std::filesystem::path(USIM_TEST_FIXTURES_DIR) /
                                                      expected::k_file_name);
    REQUIRE(pack.has_value());
    const auto chain = EphemerisChain::create(pack.value());
    REQUIRE(chain.has_value());
    CHECK(allocations_during_snapshots(chain.value(), BodyId::naif(0),
                                       Instant::from_seconds_since_j2000(3600.0), 2000.0) == 0);
}

TEST_CASE("advancing the clock and filling snapshots allocates nothing", "[allocation]") {
    namespace expected = usim::test::pack;
    auto synthetic = usim::sim::Simulation::load_file(std::filesystem::path(USIM_TEST_FIXTURES_DIR) /
                                                      expected::k_file_name);
    REQUIRE(synthetic.has_value());
    CHECK(allocations_during_fill(synthetic.value(), BodyId::naif(1000)) == 0);

    const auto path = usim::test::validation::default_pack_path();
    if (!path) {
        SKIP("no default pack in data/packs");
    }
    auto real = usim::sim::Simulation::load_file(*path);
    REQUIRE(real.has_value());
    CHECK(allocations_during_fill(real.value(), BodyId::naif(399)) == 0);
    CHECK(allocations_during_fill(real.value(), BodyId::naif(0)) == 0);
}

TEST_CASE("ephemeris evaluation allocates nothing (default pack)", "[allocation]") {
    const auto& pack = usim::test::validation::default_pack();
    const auto chain = EphemerisChain::create(pack);
    REQUIRE(chain.has_value());
    const Instant t0 = Instant::from_seconds_since_j2000(0.0);
    CHECK(allocations_during_snapshots(chain.value(), BodyId::naif(0), t0, 3.7e5) == 0);
    CHECK(allocations_during_snapshots(chain.value(), BodyId::naif(399), t0, 3.7e5) == 0);
}
